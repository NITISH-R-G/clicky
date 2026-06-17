import Foundation
import Accelerate

func oldRMS(channelSamples: UnsafeMutablePointer<Float>, frameCount: Int) -> Float {
    var summedSquares: Float = 0
    for sampleIndex in 0..<frameCount {
        let sample = channelSamples[sampleIndex]
        summedSquares += sample * sample
    }
    return sqrt(summedSquares / Float(frameCount))
}

func newRMS(channelSamples: UnsafeMutablePointer<Float>, frameCount: Int) -> Float {
    var rootMeanSquare: Float = 0
    vDSP_rmsqv(channelSamples, 1, &rootMeanSquare, vDSP_Length(frameCount))
    return rootMeanSquare
}

let frameCount = 1024
let samples = UnsafeMutablePointer<Float>.allocate(capacity: frameCount)
for i in 0..<frameCount {
    samples[i] = Float.random(in: -1.0...1.0)
}

let iterations = 100_000

let startOld = Date()
var dummy1: Float = 0
for _ in 0..<iterations {
    dummy1 += oldRMS(channelSamples: samples, frameCount: frameCount)
}
let timeOld = Date().timeIntervalSince(startOld)

let startNew = Date()
var dummy2: Float = 0
for _ in 0..<iterations {
    dummy2 += newRMS(channelSamples: samples, frameCount: frameCount)
}
let timeNew = Date().timeIntervalSince(startNew)

print("Old RMS time: \(timeOld) seconds")
print("New RMS time: \(timeNew) seconds")
print("Dummy values: \(dummy1), \(dummy2)")

samples.deallocate()
