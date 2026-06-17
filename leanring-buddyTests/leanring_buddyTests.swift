//
//  leanring_buddyTests.swift
//  leanring-buddyTests
//
//  Created by thorfinn on 3/2/26.
//

import Testing
@testable import leanring_buddy

struct leanring_buddyTests {

    @Test func firstPermissionRequestUsesSystemPromptOnly() async throws {
        let presentationDestination = WindowPositionManager.permissionRequestPresentationDestination(
            hasPermissionNow: false,
            hasAttemptedSystemPrompt: false
        )

        #expect(presentationDestination == .systemPrompt)
    }

    @Test func repeatedPermissionRequestOpensSystemSettings() async throws {
        let presentationDestination = WindowPositionManager.permissionRequestPresentationDestination(
            hasPermissionNow: false,
            hasAttemptedSystemPrompt: true
        )

        #expect(presentationDestination == .systemSettings)
    }

    @Test func knownGrantedScreenRecordingPermissionSkipsTheGate() async throws {
        let shouldTreatPermissionAsGranted = WindowPositionManager.shouldTreatScreenRecordingPermissionAsGrantedForSessionLaunch(
            hasScreenRecordingPermissionNow: false,
            hasPreviouslyConfirmedScreenRecordingPermission: true
        )

        #expect(shouldTreatPermissionAsGranted)
    }


    // MARK: - ElementLocationDetector Tests

    @Test func testParseCoordinateValidResponse() async throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "text",
                    "text": "Clicking the button"
                },
                {
                    "type": "tool_use",
                    "id": "tool_1",
                    "name": "computer",
                    "input": {
                        "action": "left_click",
                        "coordinate": [123.5, 456.7]
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!
        let detector = ElementLocationDetector(apiKey: "test")
        let coordinate = detector.parseCoordinateFromResponse(data: data)

        #expect(coordinate != nil)
        #expect(coordinate?.x == 123.5)
        #expect(coordinate?.y == 456.7)
    }

    @Test func testParseCoordinateInvalidJSON() async throws {
        let jsonString = "{ invalid json"
        let data = jsonString.data(using: .utf8)!
        let detector = ElementLocationDetector(apiKey: "test")
        let coordinate = detector.parseCoordinateFromResponse(data: data)

        #expect(coordinate == nil)
    }

    @Test func testParseCoordinateNoContentBlocks() async throws {
        let jsonString = """
        {
            "not_content": []
        }
        """
        let data = jsonString.data(using: .utf8)!
        let detector = ElementLocationDetector(apiKey: "test")
        let coordinate = detector.parseCoordinateFromResponse(data: data)

        #expect(coordinate == nil)
    }

    @Test func testParseCoordinateNoToolUseBlock() async throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "text",
                    "text": "There is no specific element to click."
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!
        let detector = ElementLocationDetector(apiKey: "test")
        let coordinate = detector.parseCoordinateFromResponse(data: data)

        #expect(coordinate == nil)
    }

    @Test func testParseCoordinateMalformedCoordinateArray() async throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "tool_use",
                    "id": "tool_1",
                    "name": "computer",
                    "input": {
                        "action": "left_click",
                        "coordinate": [123.5]
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!
        let detector = ElementLocationDetector(apiKey: "test")
        let coordinate = detector.parseCoordinateFromResponse(data: data)

        #expect(coordinate == nil)
    }
}
