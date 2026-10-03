import XCTest
@testable import IdleVizCore

final class AudioDelaySettingTests: XCTestCase {
    func testNormalizesToTheSlider() {
        XCTAssertEqual(AudioDelaySetting.normalized(0.184), 0.18, accuracy: 1e-9)
        XCTAssertEqual(AudioDelaySetting.normalized(0.186), 0.19, accuracy: 1e-9)
        XCTAssertEqual(AudioDelaySetting.normalized(-1), 0)
        XCTAssertEqual(AudioDelaySetting.normalized(9), 2.5)
        XCTAssertEqual(AudioDelaySetting.normalized(.nan), 0)
    }

    func testANewDeviceStartsAtItsReportedLatency() {
        let saved = ["airpods": 0.21]
        XCTAssertEqual(AudioDelaySetting.delay(forDevice: "airpods", saved: saved, reportedLatency: 0.15), 0.21, accuracy: 1e-9)
        XCTAssertEqual(AudioDelaySetting.delay(forDevice: "speaker", saved: saved, reportedLatency: 0.1532), 0.15, accuracy: 1e-9)
        XCTAssertEqual(AudioDelaySetting.delay(forDevice: "airplay", saved: saved, reportedLatency: 4), 2.5)
        XCTAssertEqual(AudioDelaySetting.delay(forDevice: "built-in", saved: saved, reportedLatency: 0.004), 0)
    }

    func testReadsSavedDelays() {
        let suite = "AudioDelaySettingTests-\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        XCTAssertEqual(AudioDelaySetting.saved(in: defaults), [:])
        defaults.set(["a": 0.2, "b": "text", "c": 1], forKey: AudioDelaySetting.key)
        XCTAssertEqual(AudioDelaySetting.saved(in: defaults), ["a": 0.2, "c": 1])
    }

    func testLabelAndScript() {
        XCTAssertEqual(AudioDelaySetting.label(0.18), "180 ms")
        XCTAssertEqual(AudioDelaySetting.label(0), "0 ms")
        XCTAssertEqual(AudioDelaySetting.label(2.5), "2500 ms")
        XCTAssertEqual(AudioDelaySetting.script(for: 0.25), "window.setAudioDelay?.(0.25)")
        XCTAssertEqual(AudioDelaySetting.script(for: 99), "window.setAudioDelay?.(2.5)")
    }
}

final class DelayLineTests: XCTestCase {
    func testReleasesEachFrameAfterTheDelay() {
        var line = DelayLine<Int>()
        for frame in 0..<30 { line.push(frame, at: Double(frame) / 60) }
        // At t = 0.5 s with a 0.25 s delay, the frame captured at 0.25 s (frame 15) is due.
        XCTAssertEqual(line.pop(at: 0.5, delay: 0.25), 15)
        // Nothing new is due at the same moment.
        XCTAssertNil(line.pop(at: 0.5, delay: 0.25))
        XCTAssertEqual(line.pop(at: 0.5 + 1.0 / 60, delay: 0.25), 16)
    }

    func testNothingIsDueBeforeTheDelayHasPassed() {
        var line = DelayLine<Int>()
        line.push(1, at: 10)
        XCTAssertNil(line.pop(at: 10.1, delay: 0.2))
        XCTAssertEqual(line.pop(at: 10.2, delay: 0.2), 1)
    }

    func testZeroDelayPassesStraightThrough() {
        var line = DelayLine<Int>()
        line.push(7, at: 3)
        XCTAssertEqual(line.pop(at: 3, delay: 0), 7)
        XCTAssertEqual(line.count, 0)
    }

    func testSkipsFramesWhenSeveralAreDue() {
        var line = DelayLine<Int>()
        for frame in 0..<10 { line.push(frame, at: Double(frame)) }
        XCTAssertEqual(line.pop(at: 6, delay: 1), 5)
        XCTAssertEqual(line.count, 4)
    }

    func testAShorterDelayCatchesUp() {
        var line = DelayLine<Int>()
        for frame in 0..<120 { line.push(frame, at: Double(frame) / 60) }
        XCTAssertEqual(line.pop(at: 2, delay: 1.5), 30)
        XCTAssertEqual(line.pop(at: 2, delay: 0.5), 90)
    }

    func testAnUnevenTimerDoesNotDropFrames() {
        // 60 calls a second, each up to 2 ms early or late. A delay of a whole number of frames puts
        // every frame right on the edge of being due.
        let jitter = [0.0, 0.0015, -0.002, 0.0007, -0.0012, 0.002, -0.0004]
        for delay in [0, 0.07, 0.1, 1, 2.5] {
            var line = DelayLine<Int>()
            var released = [Int]()
            for tick in 0..<600 {
                let time = Double(tick) / 60 + jitter[tick % jitter.count]
                line.push(tick, at: time)
                if let frame = line.pop(at: time, delay: delay) { released.append(frame) }
            }
            // In order, with none left out.
            XCTAssertEqual(released, Array(0..<released.count), "for \(delay) s")
            let expected = 600 - Int((delay * 60).rounded())
            XCTAssertTrue((expected - 2...expected).contains(released.count), "\(released.count) frames for \(delay) s")
        }
    }

    func testTwoDueFramesComeOutOnePerCall() {
        var line = DelayLine<Int>()
        line.push(1, at: 1)
        line.push(2, at: 2)
        XCTAssertEqual(line.pop(at: 3, delay: 0.5), 1)
        XCTAssertEqual(line.pop(at: 3, delay: 0.5), 2)
        XCTAssertNil(line.pop(at: 3, delay: 0.5))
    }

    func testStaysBounded() {
        var line = DelayLine<Int>()
        for frame in 0..<1000 { line.push(frame, at: Double(frame)) }
        XCTAssertEqual(line.count, DelayLine<Int>.capacity)
        line.removeAll()
        XCTAssertEqual(line.count, 0)
    }
}

final class DelayDetectorTests: XCTestCase {
    private let rate = 48_000.0

    /// A repeatable random source, so the tests don't flake.
    private struct Random: RandomNumberGenerator {
        var state: UInt64
        mutating func next() -> UInt64 {
            state = state &* 6_364_136_223_846_793_005 &+ 1_442_695_040_888_963_407
            return state
        }
    }

    /// Six seconds of "music": drum hits at uneven times over a quiet tone.
    private func music(seed: UInt64, seconds: Double = 6) -> [Float] {
        var random = Random(state: seed)
        let count = Int(seconds * rate)
        var samples = (0..<count).map { 0.02 * Float(sin(2 * Double.pi * 220 * Double($0) / rate)) }
        var time = 0.1
        while time < seconds - 0.2 {
            let start = Int(time * rate)
            let level = Float.random(in: 0.3...0.9, using: &random)
            for offset in 0..<Int(0.08 * rate) where start + offset < count {
                let decay = Float(exp(-Double(offset) / (0.02 * rate)))
                samples[start + offset] += level * decay * Float.random(in: -1...1, using: &random)
            }
            time += Double.random(in: 0.12...0.55, using: &random)
        }
        return samples
    }

    /// What a microphone would pick up: later, quieter, a little muffled, with room noise.
    private func heard(_ sent: [Float], delay: Double, gain: Float = 0.2, noise: Float = 0.01, seed: UInt64 = 9) -> [Float] {
        var random = Random(state: seed)
        let shift = Int(delay * rate)
        var out = [Float](repeating: 0, count: sent.count)
        var previous: Float = 0
        for index in shift..<sent.count {
            previous = previous * 0.5 + sent[index - shift] * 0.5
            out[index] = previous * gain
        }
        return out.map { $0 + noise * Float.random(in: -1...1, using: &random) }
    }

    private typealias Recording = DelayDetector.Recording

    private func recording(_ samples: [Float], start: TimeInterval = 0) -> Recording {
        Recording(samples: samples, sampleRate: rate, start: start)
    }

    private func measure(_ delay: Double, offset: Double = 0, inputLatency: Double = 0, noise: Float = 0.01) -> Double? {
        let sent = music(seed: 42)
        // A microphone recording that starts `offset` seconds later holds each sound that much earlier.
        let mic = heard(sent, delay: delay - offset, noise: noise)
        let heard = recording(mic, start: 100 + offset)
        return DelayDetector.delay(sent: recording(sent, start: 100), heard: heard, inputLatency: inputLatency)
    }

    func testFindsBluetoothAndAirPlaySizedDelays() throws {
        for delay in [0.0, 0.03, 0.18, 0.29, 1.0, 2.0, 2.4] {
            let found = try XCTUnwrap(measure(delay), "no result for \(delay) s")
            XCTAssertEqual(found, delay, accuracy: 0.011, "for \(delay) s")
        }
    }

    func testWorksThroughRoomNoise() throws {
        // Noise at a quarter of the music's level at the microphone.
        XCTAssertEqual(try XCTUnwrap(measure(0.22, noise: 0.05)), 0.22, accuracy: 0.011)
    }

    func testAllowsForRecordingsThatStartAtDifferentTimes() throws {
        XCTAssertEqual(try XCTUnwrap(measure(0.2, offset: 0.137)), 0.2, accuracy: 0.011)
        XCTAssertEqual(try XCTUnwrap(measure(0.2, offset: -0.05)), 0.2, accuracy: 0.011)
    }

    func testSubtractsTheMicrophonesOwnLatency() throws {
        XCTAssertEqual(try XCTUnwrap(measure(0.25, inputLatency: 0.04)), 0.21, accuracy: 0.011)
    }

    func testSoundThatComesBeforeTheTapHandsItOverCountsAsNoDelay() throws {
        // Built-in speakers: the microphone hears the music 40 ms before the tap delivers it.
        let sent = music(seed: 42)
        let mic = heard(sent, delay: 0.06)
        let found = DelayDetector.delay(sent: recording(sent, start: 100.1), heard: recording(mic, start: 100))
        XCTAssertEqual(try XCTUnwrap(found), 0)
        let peak = try XCTUnwrap(DelayDetector.peak(sent: recording(sent, start: 100.1), heard: recording(mic, start: 100)))
        XCTAssertEqual(peak.delay, -0.04, accuracy: 0.002)
    }

    func testEchoesDoNotHideThePeak() throws {
        // A speaker across the room: the direct sound, then reflections 35 and 60 ms behind it.
        let sent = music(seed: 42)
        let direct = heard(sent, delay: 1.95, gain: 0.05, noise: 0)
        let first = heard(sent, delay: 1.985, gain: 0.04, noise: 0)
        let second = heard(sent, delay: 2.01, gain: 0.03, noise: 0.01)
        let mic = zip(zip(direct, first), second).map { $0.0 + $0.1 + $1 }
        XCTAssertEqual(try XCTUnwrap(DelayDetector.delay(sent: recording(sent), heard: recording(mic))), 1.95, accuracy: 0.011)
    }

    func testAMicrophoneAtAnotherSampleRate() throws {
        let sent = music(seed: 7)
        let delayed = heard(sent, delay: 0.3)
        // Resample 48 kHz to 44.1 kHz by picking the nearest sample; rough, as a real one is never exact.
        let ratio = 48_000.0 / 44_100.0
        let resampled = (0..<Int(Double(delayed.count) / ratio)).map { delayed[Int(Double($0) * ratio)] }
        let found = DelayDetector.delay(sent: recording(sent), heard: Recording(samples: resampled, sampleRate: 44_100))
        XCTAssertEqual(try XCTUnwrap(found), 0.3, accuracy: 0.011)
    }

    func testGivesUpWhenTheMicrophoneHearsOnlyNoise() {
        // Headphones: the music never reaches the microphone.
        var random = Random(state: 3)
        let sent = music(seed: 42)
        let noise = sent.map { _ in 0.01 * Float.random(in: -1...1, using: &random) }
        XCTAssertNil(DelayDetector.delay(sent: recording(sent), heard: recording(noise)))
    }

    func testGivesUpWhenTheMicrophoneHearsDifferentMusic() {
        let other = heard(music(seed: 1234), delay: 0.2)
        XCTAssertNil(DelayDetector.delay(sent: recording(music(seed: 42)), heard: recording(other)))
    }

    func testGivesUpOnSilence() {
        let zeros = [Float](repeating: 0, count: Int(6 * rate))
        XCTAssertNil(DelayDetector.delay(sent: recording(zeros), heard: recording(zeros)))
        XCTAssertNil(DelayDetector.delay(sent: recording(music(seed: 42)), heard: recording(zeros)))
    }

    func testALoopStillGivesTheRightLag() throws {
        // Half a second of sound repeated exactly lines up nearly as well one repeat later. Both
        // recordings are cut from the middle of a longer stretch, as a real measurement is.
        var random = Random(state: 5)
        let loop = (0..<Int(0.5 * rate)).map { _ in Float.random(in: -0.5...0.5, using: &random) }
        let long = (0..<Int(8 * rate)).map { loop[$0 % loop.count] }
        let sent = Array(long[Int(1 * rate)..<Int(7 * rate)])
        for (delay, micStart) in [(0.2, 1.0), (0.2, 1.15), (0.31, 0.9), (0.04, 1.3)] {
            let mic = Array(heard(long, delay: delay)[Int(micStart * rate)..<Int(7 * rate)])
            let found = DelayDetector.delay(sent: recording(sent, start: 1), heard: recording(mic, start: micStart))
            XCTAssertEqual(try XCTUnwrap(found), delay, accuracy: 0.011, "for \(delay) s, microphone from \(micStart) s")
        }
    }

    func testRecordingsThatAreTooShortGiveNothing() {
        let short = music(seed: 42, seconds: 2)
        XCTAssertNil(DelayDetector.delay(sent: recording(short), heard: recording(short)))
        XCTAssertNil(DelayDetector.peak(sent: recording([]), heard: recording([])))
    }
}
