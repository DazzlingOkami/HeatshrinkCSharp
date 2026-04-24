using System;
using System.Text;
using System.IO;
using HeatshrinkCSharp;

namespace HeatshrinkTests
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Running Heatshrink C# Tests...");
            
            // Test 1: Basic compression and decompression
            Console.WriteLine("\nTest 1: Basic compression and decompression");
            TestBasicCompressionDecompression();
            
            // Test 2: Empty data
            Console.WriteLine("\nTest 2: Empty data");
            TestEmptyData();
            
            // Test 3: Repeated data
            Console.WriteLine("\nTest 3: Repeated data");
            TestRepeatedData();
            
            // Test 4: Large data
            Console.WriteLine("\nTest 4: Large data");
            TestLargeData();
            
            // Test 5: Different window and lookahead sizes
            Console.WriteLine("\nTest 5: Different window and lookahead sizes");
            TestDifferentWindowSizes();
            
            // Test 6: Compare with C version compression
            Console.WriteLine("\nTest 6: Compare with C version compression");
            TestCompareWithCVersion();
            
            Console.WriteLine("\nAll tests completed!");
            Console.ReadKey();
        }
        
        static void TestBasicCompressionDecompression()
        {
            string testString = "Hello, World! This is a test of the Heatshrink compression algorithm.";
            byte[] originalData = Encoding.UTF8.GetBytes(testString);
            
            // Compress data
            byte[] compressedData = HeatshrinkEncoder.Compress(8, 4, originalData);
            Console.WriteLine($"Original size: {originalData.Length} bytes");
            Console.WriteLine($"Compressed size: {compressedData.Length} bytes");
            
            // Decompress data
            byte[] decompressedData = HeatshrinkDecoder.Decompress(8, 4, compressedData);
            string decompressedString = Encoding.UTF8.GetString(decompressedData);
            
            // Verify
            bool success = testString == decompressedString;
            Console.WriteLine($"Test result: {(success ? "PASS" : "FAIL")}");
            if (!success)
            {
                Console.WriteLine($"Expected: {testString}");
                Console.WriteLine($"Got: {decompressedString}");
            }
        }
        
        static void TestEmptyData()
        {
            byte[] originalData = new byte[0];
            
            // Compress data
            byte[] compressedData = HeatshrinkEncoder.Compress(8, 4, originalData);
            Console.WriteLine($"Original size: {originalData.Length} bytes");
            Console.WriteLine($"Compressed size: {compressedData.Length} bytes");
            
            // Decompress data
            byte[] decompressedData = HeatshrinkDecoder.Decompress(8, 4, compressedData);
            
            // Verify
            bool success = decompressedData.Length == 0;
            Console.WriteLine($"Test result: {(success ? "PASS" : "FAIL")}");
        }
        
        static void TestRepeatedData()
        {
            // Create repeated data
            string repeatedString = new string('A', 1000);
            byte[] originalData = Encoding.UTF8.GetBytes(repeatedString);
            
            // Compress data
            byte[] compressedData = HeatshrinkEncoder.Compress(8, 4, originalData);
            Console.WriteLine($"Original size: {originalData.Length} bytes");
            Console.WriteLine($"Compressed size: {compressedData.Length} bytes");
            Console.WriteLine($"Compression ratio: {((float)compressedData.Length / originalData.Length):F4}");
            
            // Decompress data
            byte[] decompressedData = HeatshrinkDecoder.Decompress(8, 4, compressedData);
            string decompressedString = Encoding.UTF8.GetString(decompressedData);
            
            // Verify
            bool success = repeatedString == decompressedString;
            Console.WriteLine($"Test result: {(success ? "PASS" : "FAIL")}");
        }
        
        static void TestLargeData()
        {
            // Create large data
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 10000; i++)
            {
                sb.AppendLine($"Line {i}: This is a test line for compression.");
            }
            string largeString = sb.ToString();
            byte[] originalData = Encoding.UTF8.GetBytes(largeString);
            
            // Compress data
            byte[] compressedData = HeatshrinkEncoder.Compress(10, 5, originalData);
            Console.WriteLine($"Original size: {originalData.Length} bytes");
            Console.WriteLine($"Compressed size: {compressedData.Length} bytes");
            Console.WriteLine($"Compression ratio: {((float)compressedData.Length / originalData.Length):F4}");
            
            // Decompress data
            byte[] decompressedData = HeatshrinkDecoder.Decompress(10, 5, compressedData);
            string decompressedString = Encoding.UTF8.GetString(decompressedData);
            
            // Verify
            bool success = largeString == decompressedString;
            Console.WriteLine($"Test result: {(success ? "PASS" : "FAIL")}");
        }
        
        static void TestDifferentWindowSizes()
        {
            string testString = "Hello, World! This is a test of different window sizes.";
            byte[] originalData = Encoding.UTF8.GetBytes(testString);
            
            // Test with different window and lookahead sizes
            (byte window, byte lookahead)[] configurations = {
                (8, 4),
                (10, 5),
                (12, 6)
            };
            
            foreach (var (window, lookahead) in configurations)
            {
                Console.WriteLine($"\nTesting with window={window}, lookahead={lookahead}");
                
                // Compress data
                byte[] compressedData = HeatshrinkEncoder.Compress(window, lookahead, originalData);
                Console.WriteLine($"Compressed size: {compressedData.Length} bytes");
                
                // Decompress data
                byte[] decompressedData = HeatshrinkDecoder.Decompress(window, lookahead, compressedData);
                string decompressedString = Encoding.UTF8.GetString(decompressedData);
                
                // Verify
                bool success = testString == decompressedString;
                Console.WriteLine($"Test result: {(success ? "PASS" : "FAIL")}");
            }
        }
        
        static void TestCompareWithCVersion()
        {
            string testFileName = "alice29.txt";
            string cCompressedFileName = "alice29.txt.hs";
            
            Console.WriteLine($"Testing with {testFileName}...");
            
            // Read original data
            byte[] originalData = File.ReadAllBytes(testFileName);
            Console.WriteLine($"Original file size: {originalData.Length} bytes");
            
            // Read C version compressed data
            byte[] cCompressedData = File.ReadAllBytes(cCompressedFileName);
            Console.WriteLine($"C version compressed size: {cCompressedData.Length} bytes");
            double cCompressionRatio = (double)cCompressedData.Length / originalData.Length;
            Console.WriteLine($"C version compression ratio: {cCompressionRatio:F4}");
            
            // Compress with C# version using same parameters (window_sz2=10, lookahead_sz2=4)
            byte[] csCompressedData = HeatshrinkEncoder.Compress(10, 4, originalData);
            Console.WriteLine($"C# version compressed size: {csCompressedData.Length} bytes");
            double csCompressionRatio = (double)csCompressedData.Length / originalData.Length;
            Console.WriteLine($"C# version compression ratio: {csCompressionRatio:F4}");
            
            // Compare compressed data
            bool compressionMatch = csCompressedData.SequenceEqual(cCompressedData);
            Console.WriteLine($"Compression match: {(compressionMatch ? "PASS" : "FAIL")}");
            
            if (!compressionMatch)
            {
                Console.WriteLine("Compression output differs between C and C# versions.");
            }
            
            // Test decompression of C version compressed data
            byte[] decompressedFromC = HeatshrinkDecoder.Decompress(10, 4, cCompressedData);
            bool decompressionFromCMatch = decompressedFromC.SequenceEqual(originalData);
            Console.WriteLine($"Decompression from C version: {(decompressionFromCMatch ? "PASS" : "FAIL")}");
            
            // Test decompression of C# version compressed data
            byte[] decompressedFromCS = HeatshrinkDecoder.Decompress(10, 4, csCompressedData);
            bool decompressionFromCSMatch = decompressedFromCS.SequenceEqual(originalData);
            Console.WriteLine($"Decompression from C# version: {(decompressionFromCSMatch ? "PASS" : "FAIL")}");
        }
    }
}