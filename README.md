# HeatshrinkCSharp

A data compression/decompression library implemented in C#. Has the same data structures and interoperability as the [C version](https://github.com/atomicobject/heatshrink).

## Getting Started

### Basic Usage

To use HeatshrinkCSharp in your project, simply include the following files:
- `HeatshrinkCommon.cs` - Common constants and utilities
- `HeatshrinkEncoder.cs` - Compression functionality
- `HeatshrinkDecoder.cs` - Decompression functionality

### Compression Example

```csharp
using HeatshrinkCSharp;

// Compress data
byte[] originalData = System.Text.Encoding.UTF8.GetBytes("Hello, World!");
byte[] compressedData = HeatshrinkEncoder.Compress(8, 4, originalData);

// Decompress data
byte[] decompressedData = HeatshrinkDecoder.Decompress(8, 4, compressedData);
string decompressedString = System.Text.Encoding.UTF8.GetString(decompressedData);
```

### Parameters

- `window_sz2` - The window size as a power of 2 (4-15)
- `lookahead_sz2` - The lookahead size as a power of 2 (3-`window_sz2-1`)

## Testing

### Running Tests

To run the provided tests, navigate to the `test` directory and execute:

```bash
dotnet run
```

### Test Cases

The test suite includes the following tests:
1. **Basic compression and decompression** - Tests the basic functionality with a simple string
2. **Empty data** - Tests compression and decompression of empty data
3. **Repeated data** - Tests compression of highly repetitive data
4. **Large data** - Tests compression and decompression of large data
5. **Different window and lookahead sizes** - Tests with various window and lookahead configurations
6. **C version interoperability** - Tests that C# version can compress data that matches C version output and decompress data compressed by C version

## Performance

Heatshrink is designed to be a lightweight compression algorithm, optimized for embedded systems. It provides a good balance between compression ratio and speed.

### Example Performance

| Data Type | Original Size | Compressed Size | Compression Ratio |
|-----------|---------------|-----------------|-------------------|
| Simple string | 60 bytes | ~35 bytes | ~0.58 |
| Repeated data | 1000 bytes | ~20 bytes | ~0.02 |
| Large text | 10000 bytes | ~4000 bytes | ~0.40 |
| Alice in Wonderland (alice29.txt) | 152089 bytes | 85832 bytes | 0.5644 |

## License

This project is licensed under the ISC License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Based on the original [Heatshrink](https://github.com/atomicobject/heatshrink) library by Atomic Object
- Implemented in C# for cross-platform compatibility
