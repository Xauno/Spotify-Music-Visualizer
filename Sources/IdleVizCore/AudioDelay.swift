import Accelerate
import Foundation

/// The Audio delay setting: how long the visuals wait so they match what the speakers play.
/// It is saved per output device, since built-in speakers, Bluetooth and AirPlay differ a lot.
public enum AudioDelaySetting {
    /// A dictionary from the output device's UID to its delay in seconds.
    public static let key = "audioDelayByDevice"
    public static let range = 0.0...2.5
    public static let step = 0.01

    /// A delay inside the slider's range, on one of its 10 ms steps.
    public static func normalized(_ seconds: Double) -> Double {
        guard seconds.isFinite else { return 0 }
        let clamped = min(max(seconds, range.lowerBound), range.upperBound)
        return (clamped / step).rounded() * step
    }

    /// The delay to use for a device: the saved one, or for a device seen for the first time the
    /// latency macOS reports for it. That is about right for AirPlay and often a little low for
    /// Bluetooth, so it's a starting point for Detect delay, not a replacement.
    public static func delay(forDevice uid: String, saved: [String: Double], reportedLatency: Double) -> Double {
        normalized(saved[uid] ?? reportedLatency)
    }

    /// Reads the saved delays, dropping anything that isn't a number.
    public static func saved(in defaults: UserDefaults) -> [String: Double] {
        (defaults.dictionary(forKey: key) ?? [:]).compactMapValues { ($0 as? NSNumber)?.doubleValue }
    }

    public static func label(_ seconds: Double) -> String {
        "\(Int((normalized(seconds) * 1000).rounded())) ms"
    }

    /// The call that tells the page the delay, so the progress bar shows the position you hear.
    public static func script(for seconds: Double) -> String {
        "window.setAudioDelay?.(\(normalized(seconds)))"
    }
}

/// Holds analysed frames and releases each one `delay` seconds after it was captured.
public struct DelayLine<Element> {
    /// More than the longest delay at 60 frames a second.
    public static var capacity: Int { 240 }

    private var items: [(time: TimeInterval, element: Element)] = []

    public init() {}

    public mutating func push(_ element: Element, at time: TimeInterval) {
        items.append((time, element))
        if items.count > Self.capacity { items.removeFirst(items.count - Self.capacity) }
    }

    /// An element captured at or before `time - delay`, or nil if none has come due since the last
    /// call. Normally that is one element per call, in order. When more than two are due (the delay
    /// was shortened, or the caller fell behind), the newest of them is returned and the older ones
    /// are dropped.
    ///
    /// Always returning the newest would drop frames: with a delay that is a whole number of frames
    /// (every 50 ms is, at 60 frames a second) each frame comes due right as a call is made, and the
    /// timer's jitter decides whether that call gets none or the next gets two. Handing a second due
    /// element out on the next call instead costs at most one frame of extra delay.
    public mutating func pop(at time: TimeInterval, delay: TimeInterval) -> Element? {
        let due = time - delay
        guard let last = items.lastIndex(where: { $0.time <= due }) else { return nil }
        let index = last >= 2 ? last : 0
        let element = items[index].element
        items.removeFirst(index + 1)
        return element
    }

    public mutating func removeAll() {
        items.removeAll()
    }

    public var count: Int { items.count }
}

/// Measures how far the sound from the speakers lags behind the audio Spotify sent, from two
/// recordings of the same few seconds: the tap's signal and the microphone's.
///
/// The two waveforms are cross-correlated with a softened phase transform (GCC-PHAT): every
/// frequency counts nearly the same however loud it is, so the tone of the speakers and the room
/// matters little and the right lag shows up as a sharp spike with its echoes just behind it.
public enum DelayDetector {
    /// Only these frequencies are compared: small speakers play little below, and above the
    /// microphone hears mostly noise.
    static let band = 300.0...6000.0
    /// The peak has to stand this many standard deviations above the other lags…
    static let minimumZScore = 10.0
    /// …and this far above the best lag elsewhere, or a steady beat could match one beat late.
    static let minimumPeakRatio = 1.5
    /// How much of each frequency's loudness is divided away. 1 is the pure phase transform;
    /// a little less keeps frequencies the microphone barely hears from adding only noise.
    static let whitening: Float = 0.8
    /// The correlation is averaged over this many seconds, which adds a spike's closest echoes to it.
    static let echoWindow = 0.005
    /// Lags this close to the peak, in seconds, belong to it: echoes off the desk and the walls.
    static let peakWidth = 0.1
    /// The search starts a little below zero: built-in speakers can sound slightly before the tap
    /// hands the audio over. Such a result is saved as no delay.
    static let earliestLag = -0.1

    /// A few seconds of audio from one source.
    public struct Recording: Sendable {
        /// Mono samples.
        public var samples: [Float]
        public var sampleRate: Double
        /// The time of the first sample, in seconds on a clock both recordings share.
        public var start: TimeInterval

        public init(samples: [Float], sampleRate: Double, start: TimeInterval = 0) {
            self.samples = samples
            self.sampleRate = sampleRate
            self.start = start
        }
    }

    /// The best lag found, and how clearly it stands out.
    public struct Peak: Sendable, Equatable {
        /// Seconds the heard recording lags behind the sent one.
        public var delay: Double
        /// How far the peak stands above the other lags, in standard deviations.
        public var zScore: Double
        /// The peak over the best lag elsewhere.
        public var ratio: Double

        /// Whether the peak is clear enough to trust.
        public var isClear: Bool {
            zScore >= DelayDetector.minimumZScore && ratio >= DelayDetector.minimumPeakRatio
        }
    }

    /// Cross-correlates the two recordings over lags of just below 0 to 2.5 s and returns the best one, clear or not.
    /// - Parameter inputLatency: The microphone's own latency in seconds, which is not part of the speakers' delay.
    /// - Returns: Nil if the recordings are too short or one of them is silent.
    public static func peak(sent: Recording, heard: Recording, inputLatency: Double = 0) -> Peak? {
        let rate = sent.sampleRate
        let maxLag = AudioDelaySetting.range.upperBound
        guard rate > 0, heard.sampleRate > 0 else { return nil }
        let heardSamples = resample(heard.samples, from: heard.sampleRate, to: rate)
        guard Double(sent.samples.count) > maxLag * rate, Double(heardSamples.count) > maxLag * rate else { return nil }

        // Long enough that the correlation doesn't wrap around.
        let log2n = vDSP_Length((Double(sent.samples.count + heardSamples.count)).logC2())
        let count = 1 << Int(log2n)
        guard let correlation = phaseCorrelation(sent: sent.samples, heard: heardSamples, log2n: log2n, sampleRate: rate) else {
            return nil
        }

        // correlation[shift] compares sent[t] with heard[t + shift]; a negative shift wraps to the end.
        // The microphone recording started `offset` seconds after the tap's, so a lag is `shift / rate + offset`.
        // The sound reached the microphone `inputLatency` before it reached the recording.
        let offset = heard.start - inputLatency - sent.start
        let firstShift = Int(((earliestLag - offset) * rate).rounded())
        let shifts = Int((maxLag - earliestLag) * rate) + 1
        var values = [Float](repeating: 0, count: shifts)
        for index in 0..<shifts {
            let shift = firstShift + index
            guard abs(shift) < count / 2 else { continue }
            let value = correlation[(shift + count) % count]
            values[index] = value * value
        }
        values = smoothedRoot(values, window: max(1, Int(echoWindow * rate)))

        guard let peak = values.indices.max(by: { values[$0] < values[$1] }), values[peak] > 0 else { return nil }
        let width = Int(peakWidth * rate)
        var others = [Float]()
        others.reserveCapacity(values.count)
        if peak - width > 0 { others.append(contentsOf: values[..<(peak - width)]) }
        if peak + width + 1 < values.count { others.append(contentsOf: values[(peak + width + 1)...]) }
        guard others.count > 2 else { return nil }
        let mean = vDSP.mean(others)
        let deviation = sqrt(vDSP.meanSquare(vDSP.add(-mean, others)))
        let runnerUp = vDSP.maximum(others)
        guard deviation > 0, runnerUp > 0 else { return nil }
        return Peak(
            delay: Double(firstShift + peak) / rate + offset,
            zScore: Double((values[peak] - mean) / deviation),
            ratio: Double(values[peak] / runnerUp)
        )
    }

    /// The whole measurement: both recordings in, the delay to save out.
    /// - Returns: Nil if no lag stands out clearly: too quiet, a noisy room, or headphones.
    public static func delay(sent: Recording, heard: Recording, inputLatency: Double = 0) -> Double? {
        delay(from: peak(sent: sent, heard: heard, inputLatency: inputLatency))
    }

    /// The delay to save for a peak, or nil if there is none or it isn't clear enough.
    public static func delay(from peak: Peak?) -> Double? {
        guard let peak, peak.isClear else { return nil }
        return AudioDelaySetting.normalized(peak.delay)
    }

    /// The cross-correlation of the two signals with every frequency in `band` weighted equally.
    /// Nil if there is nothing to compare, for example silence.
    private static func phaseCorrelation(sent: [Float], heard: [Float], log2n: vDSP_Length, sampleRate: Double) -> [Float]? {
        let count = 1 << Int(log2n)
        let half = count / 2
        guard let setup = vDSP_create_fftsetup(log2n, FFTRadix(kFFTRadix2)) else { return nil }
        defer { vDSP_destroy_fftsetup(setup) }

        var sentReal = [Float](repeating: 0, count: half)
        var sentImaginary = [Float](repeating: 0, count: half)
        var heardReal = [Float](repeating: 0, count: half)
        var heardImaginary = [Float](repeating: 0, count: half)
        var result = [Float](repeating: 0, count: count)
        var found = false

        sentReal.withUnsafeMutableBufferPointer { sentRealPointer in
            sentImaginary.withUnsafeMutableBufferPointer { sentImaginaryPointer in
                heardReal.withUnsafeMutableBufferPointer { heardRealPointer in
                    heardImaginary.withUnsafeMutableBufferPointer { heardImaginaryPointer in
                        var sentSplit = DSPSplitComplex(realp: sentRealPointer.baseAddress!, imagp: sentImaginaryPointer.baseAddress!)
                        var heardSplit = DSPSplitComplex(realp: heardRealPointer.baseAddress!, imagp: heardImaginaryPointer.baseAddress!)
                        transform(sent, into: &sentSplit, count: count, log2n: log2n, setup: setup)
                        transform(heard, into: &heardSplit, count: count, log2n: log2n, setup: setup)

                        // heard × conj(sent), scaled to length one inside the band and zero outside it.
                        let binWidth = sampleRate / Double(count)
                        let lowest = max(1, Int((band.lowerBound / binWidth).rounded(.up)))
                        let highest = min(half - 1, Int(band.upperBound / binWidth))
                        for bin in 0..<half {
                            guard bin >= lowest, bin <= highest else {
                                heardSplit.realp[bin] = 0
                                heardSplit.imagp[bin] = 0
                                continue
                            }
                            let real = heardSplit.realp[bin] * sentSplit.realp[bin] + heardSplit.imagp[bin] * sentSplit.imagp[bin]
                            let imaginary = heardSplit.imagp[bin] * sentSplit.realp[bin] - heardSplit.realp[bin] * sentSplit.imagp[bin]
                            let magnitude = (real * real + imaginary * imaginary).squareRoot()
                            if magnitude > 0 { found = true }
                            let weight = magnitude > 0 ? 1 / pow(magnitude, whitening) : 0
                            heardSplit.realp[bin] = real * weight
                            heardSplit.imagp[bin] = imaginary * weight
                        }

                        vDSP_fft_zrip(setup, &heardSplit, 1, log2n, FFTDirection(kFFTDirection_Inverse))
                        result.withUnsafeMutableBytes { bytes in
                            vDSP_ztoc(&heardSplit, 1, bytes.bindMemory(to: DSPComplex.self).baseAddress!, 2, vDSP_Length(half))
                        }
                    }
                }
            }
        }
        return found ? result : nil
    }

    /// The square root of the average of `squares` over `window` values centred on each one.
    private static func smoothedRoot(_ squares: [Float], window: Int) -> [Float] {
        var sums = [Double](repeating: 0, count: squares.count + 1)
        for index in squares.indices { sums[index + 1] = sums[index] + Double(squares[index]) }
        return squares.indices.map { index in
            let first = max(0, index - window / 2)
            let last = min(squares.count, first + window)
            return Float(((sums[last] - sums[first]) / Double(last - first)).squareRoot())
        }
    }

    /// The forward transform of `samples`, padded with zeros to `count`.
    private static func transform(
        _ samples: [Float], into split: inout DSPSplitComplex, count: Int, log2n: vDSP_Length, setup: FFTSetup
    ) {
        var padded = [Float](repeating: 0, count: count)
        padded.replaceSubrange(0..<min(samples.count, count), with: samples.prefix(count))
        padded.withUnsafeBytes { bytes in
            vDSP_ctoz(bytes.bindMemory(to: DSPComplex.self).baseAddress!, 2, &split, 1, vDSP_Length(count / 2))
        }
        vDSP_fft_zrip(setup, &split, 1, log2n, FFTDirection(kFFTDirection_Forward))
    }

    /// Straight-line resampling. Good enough here: the comparison stops at 6 kHz.
    static func resample(_ samples: [Float], from rate: Double, to target: Double) -> [Float] {
        guard rate != target, samples.count > 1 else { return samples }
        let step = rate / target
        let count = Int(Double(samples.count - 1) / step) + 1
        return (0..<count).map { index in
            let position = Double(index) * step
            let lower = Int(position)
            let fraction = Float(position - Double(lower))
            let upper = min(lower + 1, samples.count - 1)
            return samples[lower] + (samples[upper] - samples[lower]) * fraction
        }
    }
}

private extension Double {
    /// The power of two that holds this many samples, as an exponent.
    func logC2() -> Double { Foundation.log2(self).rounded(.up) }
}
