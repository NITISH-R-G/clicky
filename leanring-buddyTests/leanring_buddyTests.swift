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

}

struct BuddyWAVFileBuilderTests {

    @Test func buildWAVDataValidatesHeaderAndContentForHappyPath() async throws {
        // Given
        let rawAudioBytes: [UInt8] = [0x01, 0x02, 0x03, 0x04]
        let pcmData = Data(rawAudioBytes)
        let sampleRate = 44100
        let channelCount = 1
        let bitsPerSample = 16

        // When
        let wavData = BuddyWAVFileBuilder.buildWAVData(
            fromPCM16MonoAudio: pcmData,
            sampleRate: sampleRate,
            channelCount: channelCount,
            bitsPerSample: bitsPerSample
        )

        // Then
        #expect(wavData.count == 44 + rawAudioBytes.count)

        // Verify ChunkID "RIFF"
        let riffHeader = String(data: wavData[0..<4], encoding: .ascii)
        #expect(riffHeader == "RIFF")

        // Verify ChunkSize (36 + data size)
        let expectedFileSize = UInt32(36 + rawAudioBytes.count)
        let actualFileSize = wavData[4..<8].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(actualFileSize == expectedFileSize)

        // Verify Format "WAVE"
        let waveHeader = String(data: wavData[8..<12], encoding: .ascii)
        #expect(waveHeader == "WAVE")

        // Verify Subchunk1ID "fmt "
        let fmtHeader = String(data: wavData[12..<16], encoding: .ascii)
        #expect(fmtHeader == "fmt ")

        // Verify Subchunk1Size (16 for PCM)
        let fmtChunkSize = wavData[16..<20].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(fmtChunkSize == 16)

        // Verify AudioFormat (1 for PCM)
        let audioFormat = wavData[20..<22].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(audioFormat == 1)

        // Verify NumChannels
        let numChannels = wavData[22..<24].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(numChannels == UInt16(channelCount))

        // Verify SampleRate
        let actualSampleRate = wavData[24..<28].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(actualSampleRate == UInt32(sampleRate))

        // Verify ByteRate
        let expectedByteRate = UInt32(sampleRate * channelCount * bitsPerSample / 8)
        let actualByteRate = wavData[28..<32].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(actualByteRate == expectedByteRate)

        // Verify BlockAlign
        let expectedBlockAlign = UInt16(channelCount * bitsPerSample / 8)
        let actualBlockAlign = wavData[32..<34].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(actualBlockAlign == expectedBlockAlign)

        // Verify BitsPerSample
        let actualBitsPerSample = wavData[34..<36].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(actualBitsPerSample == UInt16(bitsPerSample))

        // Verify Subchunk2ID "data"
        let dataHeader = String(data: wavData[36..<40], encoding: .ascii)
        #expect(dataHeader == "data")

        // Verify Subchunk2Size (data size)
        let dataSize = wavData[40..<44].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(dataSize == UInt32(rawAudioBytes.count))

        // Verify Data
        let actualData = wavData[44...]
        #expect(actualData == pcmData)
    }

    @Test func buildWAVDataHandlesEmptyDataGracefully() async throws {
        // Given
        let pcmData = Data()
        let sampleRate = 16000

        // When
        let wavData = BuddyWAVFileBuilder.buildWAVData(
            fromPCM16MonoAudio: pcmData,
            sampleRate: sampleRate
        )

        // Then
        #expect(wavData.count == 44) // Only headers

        // Verify FileSize correctly accounts for empty data
        let actualFileSize = wavData[4..<8].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(actualFileSize == 36)

        // Verify Subchunk2Size (data size) is 0
        let dataSize = wavData[40..<44].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(dataSize == 0)
    }
}
