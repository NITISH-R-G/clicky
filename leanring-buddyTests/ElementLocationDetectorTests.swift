import Testing
@testable import leanring_buddy

struct ElementLocationDetectorTests {
    @Test func resolutionMatches4by3() async throws {
        let detector = ElementLocationDetector(apiKey: "test")
        let resolution = detector.bestComputerUseResolution(forDisplayWidth: 1024, displayHeight: 768)
        #expect(resolution.width == 1024)
        #expect(resolution.height == 768)
    }

    @Test func resolutionMatches16by10() async throws {
        let detector = ElementLocationDetector(apiKey: "test")
        let resolution = detector.bestComputerUseResolution(forDisplayWidth: 2560, displayHeight: 1600)
        #expect(resolution.width == 1280)
        #expect(resolution.height == 800)
    }

    @Test func resolutionMatches16by9() async throws {
        let detector = ElementLocationDetector(apiKey: "test")
        let resolution = detector.bestComputerUseResolution(forDisplayWidth: 1920, displayHeight: 1080)
        #expect(resolution.width == 1366)
        #expect(resolution.height == 768)
    }

    @Test func resolutionDefaultsToWidestForUltrawide() async throws {
        let detector = ElementLocationDetector(apiKey: "test")
        let resolution = detector.bestComputerUseResolution(forDisplayWidth: 3440, displayHeight: 1440)
        #expect(resolution.width == 1366)
        #expect(resolution.height == 768)
    }

    @Test func resolutionDefaultsToNarrowestForPortrait() async throws {
        let detector = ElementLocationDetector(apiKey: "test")
        let resolution = detector.bestComputerUseResolution(forDisplayWidth: 1080, displayHeight: 1920)
        #expect(resolution.width == 1024)
        #expect(resolution.height == 768)
    }

    @Test func zeroHeightAvoidsCrash() async throws {
        let detector = ElementLocationDetector(apiKey: "test")
        let resolution = detector.bestComputerUseResolution(forDisplayWidth: 1024, displayHeight: 0)
        // max(1, 0) -> 1
        // displayAspectRatio becomes 1024.0 / 1.0 = 1024.0
        // Should not crash and should pick a valid resolution.
        #expect(resolution.width == 1366) // 1366/768 is closest to 1024.0
        #expect(resolution.height == 768)
    }
}
