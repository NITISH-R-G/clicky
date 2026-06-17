import Foundation
import ScreenCaptureKit

let iterations = 100000

// Baseline: create new object inside loop
let startTime = CFAbsoluteTimeGetCurrent()
for i in 0..<iterations {
    let configuration = SCStreamConfiguration()
    configuration.width = 1280
    configuration.height = 720
}
let time1 = CFAbsoluteTimeGetCurrent() - startTime
print("Baseline (new instantiation each time): \(time1) seconds")

// Optimized: reuse object
let configuration2 = SCStreamConfiguration()
let startTime2 = CFAbsoluteTimeGetCurrent()
for i in 0..<iterations {
    configuration2.width = 1280
    configuration2.height = 720
}
let time2 = CFAbsoluteTimeGetCurrent() - startTime2
print("Optimized (reusing instance): \(time2) seconds")
