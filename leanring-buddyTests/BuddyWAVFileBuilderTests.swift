//
//  BuddyWAVFileBuilderTests.swift
//  leanring-buddyTests
//
//  Created by Jules on 6/17/24.
//

import Testing
import Foundation
@testable import leanring_buddy

struct BuddyWAVFileBuilderTests {

    @Test func testWAVHeaderStructure() async throws {
        let pcmData = Data([0x01, 0x02, 0x03, 0x04])
        let wavData = BuddyWAVFileBuilder.buildWAVData(
            fromPCM16MonoAudio: pcmData,
            sampleRate: 16000,
            channelCount: 1,
            bitsPerSample: 16
        )

        // Ensure total size is correct: 44 bytes header + 4 bytes data
        #expect(wavData.count == 48)

        // Verify "RIFF" chunk ID (bytes 0-3)
        let riff = String(data: wavData[0..<4], encoding: .ascii)
        #expect(riff == "RIFF")

        // Verify "WAVE" format (bytes 8-11)
        let wave = String(data: wavData[8..<12], encoding: .ascii)
        #expect(wave == "WAVE")

        // Verify "fmt " sub-chunk ID (bytes 12-15)
        let fmt = String(data: wavData[12..<16], encoding: .ascii)
        #expect(fmt == "fmt ")

        // Verify "data" sub-chunk ID (bytes 36-39)
        let dataStr = String(data: wavData[36..<40], encoding: .ascii)
        #expect(dataStr == "data")

        // Verify actual audio data is at the end
        let payload = wavData[44..<48]
        #expect(payload == pcmData)
    }

    @Test func testWAVFileCalculations() async throws {
        // Test with standard 44.1kHz stereo 16-bit
        let sampleRate = 44100
        let channelCount = 2
        let bitsPerSample = 16
        let pcmData = Data(repeating: 0x00, count: 100) // 100 bytes

        let wavData = BuddyWAVFileBuilder.buildWAVData(
            fromPCM16MonoAudio: pcmData,
            sampleRate: sampleRate,
            channelCount: channelCount,
            bitsPerSample: bitsPerSample
        )

        // Expected values based on formula:
        let expectedByteRate = sampleRate * channelCount * bitsPerSample / 8
        let expectedBlockAlign = channelCount * bitsPerSample / 8
        let expectedFileSize = UInt32(36) + UInt32(pcmData.count)
        let expectedDataChunkSize = UInt32(pcmData.count)

        // Verify file size (bytes 4-7)
        let fileSizeValue = wavData[4..<8].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(fileSizeValue == expectedFileSize)

        // Verify subchunk1Size (16 for PCM) (bytes 16-19)
        let subchunk1Size = wavData[16..<20].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(subchunk1Size == 16)

        // Verify audio format (1 for PCM) (bytes 20-21)
        let audioFormat = wavData[20..<22].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(audioFormat == 1)

        // Verify num channels (bytes 22-23)
        let numChannels = wavData[22..<24].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(numChannels == channelCount)

        // Verify sample rate (bytes 24-27)
        let parsedSampleRate = wavData[24..<28].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(parsedSampleRate == sampleRate)

        // Verify byte rate (bytes 28-31)
        let byteRate = wavData[28..<32].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(byteRate == expectedByteRate)

        // Verify block align (bytes 32-33)
        let blockAlign = wavData[32..<34].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(blockAlign == expectedBlockAlign)

        // Verify bits per sample (bytes 34-35)
        let bps = wavData[34..<36].withUnsafeBytes { $0.load(as: UInt16.self) }
        #expect(bps == bitsPerSample)

        // Verify data chunk size (bytes 40-43)
        let dataChunkSize = wavData[40..<44].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(dataChunkSize == expectedDataChunkSize)
    }

    @Test func testWAVDataWithEmptyPCM() async throws {
        let pcmData = Data()
        let wavData = BuddyWAVFileBuilder.buildWAVData(
            fromPCM16MonoAudio: pcmData,
            sampleRate: 8000,
            channelCount: 1,
            bitsPerSample: 16
        )

        // Ensure total size is 44 bytes exactly
        #expect(wavData.count == 44)

        // File size should be 36
        let fileSizeValue = wavData[4..<8].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(fileSizeValue == 36)

        // Data chunk size should be 0
        let dataChunkSize = wavData[40..<44].withUnsafeBytes { $0.load(as: UInt32.self) }
        #expect(dataChunkSize == 0)
    }

}
