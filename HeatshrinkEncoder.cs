namespace HeatshrinkCSharp
{
    public enum HSE_sink_res
    {
        HSER_SINK_OK = 0,               /* data sunk into input buffer */
        HSER_SINK_ERROR_NULL = -1,      /* NULL argument */
        HSER_SINK_ERROR_MISUSE = -2,    /* API misuse */
    }

    public enum HSE_poll_res
    {
        HSER_POLL_EMPTY = 0,            /* input exhausted */
        HSER_POLL_MORE = 1,             /* poll again for more output  */
        HSER_POLL_ERROR_NULL = -1,      /* NULL argument */
        HSER_POLL_ERROR_MISUSE = -2,    /* API misuse */
    }

    public enum HSE_finish_res
    {
        HSER_FINISH_DONE = 0,           /* encoding is complete */
        HSER_FINISH_MORE = 1,           /* more output remaining; use poll */
        HSER_FINISH_ERROR_NULL = -1,    /* NULL argument */
    }

    internal enum HSE_state
    {
        HSES_NOT_FULL,                  /* input buffer not full enough */
        HSES_FILLED,                    /* buffer is full */
        HSES_SEARCH,                    /* searching for patterns */
        HSES_YIELD_TAG_BIT,             /* yield tag bit */
        HSES_YIELD_LITERAL,             /* emit literal byte */
        HSES_YIELD_BR_INDEX,            /* yielding backref index */
        HSES_YIELD_BR_LENGTH,           /* yielding backref length */
        HSES_SAVE_BACKLOG,              /* copying buffer to backlog */
        HSES_FLUSH_BITS,                /* flush bit buffer */
        HSES_DONE,                      /* done */
    }

    internal class OutputInfo
    {
        public byte[] Buf { get; set; }
        public int BufSize { get; set; }
        public int OutputSize { get; set; }
    }

    internal class HsIndex
    {
        public ushort Size { get; set; }
        public short[] Index { get; set; }
    }

    public class HeatshrinkEncoder
    {
        private ushort input_size;      /* bytes in input buffer */
        private ushort match_scan_index;
        private ushort match_length;
        private ushort match_pos;
        private ushort outgoing_bits;   /* enqueued outgoing bits */
        private byte outgoing_bits_count;
        private byte flags;
        private HSE_state state;        /* current state machine node */
        private byte current_byte;      /* current byte of output */
        private byte bit_index;         /* current bit index */
        private byte window_sz2;        /* 2^n size of window */
        private byte lookahead_sz2;     /* 2^n size of lookahead */
        private HsIndex search_index;
        private byte[] buffer;          /* input buffer and / sliding window for expansion */

        // Encoder flags
        private const byte FLAG_IS_FINISHING = 0x01;

        public HeatshrinkEncoder(byte window_sz2, byte lookahead_sz2)
        {
            if (window_sz2 < HeatshrinkCommon.HEATSHRINK_MIN_WINDOW_BITS ||
                window_sz2 > HeatshrinkCommon.HEATSHRINK_MAX_WINDOW_BITS ||
                lookahead_sz2 < HeatshrinkCommon.HEATSHRINK_MIN_LOOKAHEAD_BITS ||
                lookahead_sz2 >= window_sz2)
            {
                throw new ArgumentException("Invalid window or lookahead size");
            }

            this.window_sz2 = window_sz2;
            this.lookahead_sz2 = lookahead_sz2;
            int buf_sz = 2 << window_sz2;
            this.buffer = new byte[buf_sz];
            Reset();

            // Initialize search index
            int index_sz = buf_sz;
            this.search_index = new HsIndex
            {
                Size = (ushort)index_sz,
                Index = new short[index_sz]
            };
        }

        public void Reset()
        {
            Array.Clear(buffer, 0, buffer.Length);
            input_size = 0;
            state = HSE_state.HSES_NOT_FULL;
            match_scan_index = 0;
            flags = 0;
            bit_index = 0x80;
            current_byte = 0x00;
            match_length = 0;
            outgoing_bits = 0x0000;
            outgoing_bits_count = 0;
        }

        public HSE_sink_res Sink(byte[] in_buf, int size, ref int input_size_out)
        {
            if (in_buf == null)
            {
                return HSE_sink_res.HSER_SINK_ERROR_NULL;
            }

            if (IsFinishing())
            {
                return HSE_sink_res.HSER_SINK_ERROR_MISUSE;
            }

            if (state != HSE_state.HSES_NOT_FULL)
            {
                return HSE_sink_res.HSER_SINK_ERROR_MISUSE;
            }

            int write_offset = GetInputOffset() + input_size;
            int ibs = GetInputBufferSize();
            int rem = ibs - input_size;
            int cp_sz = rem < size ? rem : size;

            Array.Copy(in_buf, 0, buffer, write_offset, cp_sz);
            input_size_out = cp_sz;
            input_size += (ushort)cp_sz;

            if (cp_sz == rem)
            {
                state = HSE_state.HSES_FILLED;
            }

            return HSE_sink_res.HSER_SINK_OK;
        }

        public HSE_poll_res Poll(byte[] out_buf, int out_buf_size, ref int output_size)
        {
            if (out_buf == null)
            {
                return HSE_poll_res.HSER_POLL_ERROR_NULL;
            }

            if (out_buf_size == 0)
            {
                return HSE_poll_res.HSER_POLL_ERROR_MISUSE;
            }

            output_size = 0;
            OutputInfo oi = new OutputInfo
            {
                Buf = out_buf,
                BufSize = out_buf_size,
                OutputSize = 0
            };

            while (true)
            {
                HSE_state in_state = state;
                switch (in_state)
                {
                    case HSE_state.HSES_NOT_FULL:
                        output_size = oi.OutputSize;
                        return HSE_poll_res.HSER_POLL_EMPTY;
                    case HSE_state.HSES_FILLED:
                        DoIndexing();
                        state = HSE_state.HSES_SEARCH;
                        break;
                    case HSE_state.HSES_SEARCH:
                        state = StStepSearch();
                        break;
                    case HSE_state.HSES_YIELD_TAG_BIT:
                        state = StYieldTagBit(oi);
                        break;
                    case HSE_state.HSES_YIELD_LITERAL:
                        state = StYieldLiteral(oi);
                        break;
                    case HSE_state.HSES_YIELD_BR_INDEX:
                        state = StYieldBrIndex(oi);
                        break;
                    case HSE_state.HSES_YIELD_BR_LENGTH:
                        state = StYieldBrLength(oi);
                        break;
                    case HSE_state.HSES_SAVE_BACKLOG:
                        state = StSaveBacklog();
                        break;
                    case HSE_state.HSES_FLUSH_BITS:
                        state = StFlushBitBuffer(oi);
                        break;
                    case HSE_state.HSES_DONE:
                        output_size = oi.OutputSize;
                        return HSE_poll_res.HSER_POLL_EMPTY;
                    default:
                        output_size = oi.OutputSize;
                        return HSE_poll_res.HSER_POLL_ERROR_MISUSE;
                }

                if (state == in_state)
                {
                    if (oi.OutputSize == out_buf_size)
                    {
                        output_size = oi.OutputSize;
                        return HSE_poll_res.HSER_POLL_MORE;
                    }
                }
            }
        }

        public HSE_finish_res Finish()
        {
            flags |= FLAG_IS_FINISHING;
            if (state == HSE_state.HSES_NOT_FULL)
            {
                state = HSE_state.HSES_FILLED;
            }
            return state == HSE_state.HSES_DONE ? HSE_finish_res.HSER_FINISH_DONE : HSE_finish_res.HSER_FINISH_MORE;
        }

        private HSE_state StStepSearch()
        {
            int window_length = GetInputBufferSize();
            int lookahead_sz = GetLookaheadSize();
            ushort msi = match_scan_index;

            bool fin = IsFinishing();
            if (msi > input_size - (fin ? 1 : lookahead_sz))
            {
                return fin ? HSE_state.HSES_FLUSH_BITS : HSE_state.HSES_SAVE_BACKLOG;
            }

            int input_offset = GetInputOffset();
            int end = input_offset + msi;
            int start = end - window_length;
            if (start < 0)
            {
                start = 0;
            }

            int max_possible = lookahead_sz;
            if (input_size - msi < lookahead_sz)
            {
                max_possible = input_size - msi;
            }

            ushort match_length_out = 0;
            ushort match_pos_out = FindLongestMatch(start, end, (ushort)max_possible, ref match_length_out);

            if (match_pos_out == HeatshrinkCommon.MATCH_NOT_FOUND)
            {
                match_scan_index++;
                match_length = 0;
                return HSE_state.HSES_YIELD_TAG_BIT;
            }
            else
            {
                match_pos = match_pos_out;
                match_length = match_length_out;
                return HSE_state.HSES_YIELD_TAG_BIT;
            }
        }

        private HSE_state StYieldTagBit(OutputInfo oi)
        {
            if (CanTakeByte(oi))
            {
                if (match_length == 0)
                {
                    AddTagBit(oi, (byte)HeatshrinkCommon.HEATSHRINK_LITERAL_MARKER);
                    return HSE_state.HSES_YIELD_LITERAL;
                }
                else
                {
                    AddTagBit(oi, (byte)HeatshrinkCommon.HEATSHRINK_BACKREF_MARKER);
                    outgoing_bits = (ushort)(match_pos - 1);
                    outgoing_bits_count = window_sz2;
                    return HSE_state.HSES_YIELD_BR_INDEX;
                }
            }
            else
            {
                return HSE_state.HSES_YIELD_TAG_BIT;
            }
        }

        private HSE_state StYieldLiteral(OutputInfo oi)
        {
            if (CanTakeByte(oi))
            {
                PushLiteralByte(oi);
                return HSE_state.HSES_SEARCH;
            }
            else
            {
                return HSE_state.HSES_YIELD_LITERAL;
            }
        }

        private HSE_state StYieldBrIndex(OutputInfo oi)
        {
            if (CanTakeByte(oi))
            {
                if (PushOutgoingBits(oi) > 0)
                {
                    return HSE_state.HSES_YIELD_BR_INDEX;
                }
                else
                {
                    outgoing_bits = (ushort)(match_length - 1);
                    outgoing_bits_count = lookahead_sz2;
                    return HSE_state.HSES_YIELD_BR_LENGTH;
                }
            }
            else
            {
                return HSE_state.HSES_YIELD_BR_INDEX;
            }
        }

        private HSE_state StYieldBrLength(OutputInfo oi)
        {
            if (CanTakeByte(oi))
            {
                if (PushOutgoingBits(oi) > 0)
                {
                    return HSE_state.HSES_YIELD_BR_LENGTH;
                }
                else
                {
                    match_scan_index += match_length;
                    match_length = 0;
                    return HSE_state.HSES_SEARCH;
                }
            }
            else
            {
                return HSE_state.HSES_YIELD_BR_LENGTH;
            }
        }

        private HSE_state StSaveBacklog()
        {
            SaveBacklog();
            return HSE_state.HSES_NOT_FULL;
        }

        private HSE_state StFlushBitBuffer(OutputInfo oi)
        {
            if (bit_index == 0x80)
            {
                return HSE_state.HSES_DONE;
            }
            else if (CanTakeByte(oi))
            {
                oi.Buf[oi.OutputSize++] = current_byte;
                return HSE_state.HSES_DONE;
            }
            else
            {
                return HSE_state.HSES_FLUSH_BITS;
            }
        }

        private void AddTagBit(OutputInfo oi, byte tag)
        {
            PushBits(1, tag, oi);
        }

        private int GetInputOffset()
        {
            return GetInputBufferSize();
        }

        private int GetInputBufferSize()
        {
            return 1 << window_sz2;
        }

        private int GetLookaheadSize()
        {
            return 1 << lookahead_sz2;
        }

        private void DoIndexing()
        {
            HsIndex hsi = search_index;
            short[] last = new short[256];
            Array.Fill(last, (short)-1);

            byte[] data = buffer;
            short[] index = hsi.Index;

            int input_offset = GetInputOffset();
            int end = input_offset + input_size;

            // Build index for the input buffer
            for (int i = 0; i < end; i++)
            {
                byte v = data[i];
                short lv = last[v];
                index[i] = lv;
                last[v] = (short)i;
            }
        }

        private bool IsFinishing()
        {
            return (flags & FLAG_IS_FINISHING) != 0;
        }

        private bool CanTakeByte(OutputInfo oi)
        {
            return oi.OutputSize < oi.BufSize;
        }

        private ushort FindLongestMatch(int start, int end, ushort maxlen, ref ushort match_length)
        {
            byte[] buf = buffer;
            ushort match_maxlen = 0;
            ushort match_index = HeatshrinkCommon.MATCH_NOT_FOUND;

            ushort len = 0;

            HsIndex hsi = search_index;
            short pos = hsi.Index[end];

            while (pos - start >= 0)
            {
                len = 0;

                if (buf[pos + match_maxlen] != buf[end + match_maxlen])
                {
                    pos = hsi.Index[pos];
                    continue;
                }

                for (len = 1; len < maxlen; len++)
                {
                    if (buf[pos + len] != buf[end + len])
                    {
                        break;
                    }
                }

                if (len > match_maxlen)
                {
                    match_maxlen = len;
                    match_index = (ushort)pos;
                    if (len == maxlen)
                    {
                        break;
                    }
                }
                pos = hsi.Index[pos];
            }

            int break_even_point = 1 + window_sz2 + lookahead_sz2;

            if (match_maxlen > (break_even_point / 8))
            {
                match_length = match_maxlen;
                return (ushort)(end - match_index);
            }
            return HeatshrinkCommon.MATCH_NOT_FOUND;
        }

        private byte PushOutgoingBits(OutputInfo oi)
        {
            byte count = 0;
            byte bits = 0;
            if (outgoing_bits_count > 8)
            {
                count = 8;
                bits = (byte)(outgoing_bits >> (outgoing_bits_count - 8));
            }
            else
            {
                count = outgoing_bits_count;
                bits = (byte)outgoing_bits;
            }

            if (count > 0)
            {
                PushBits(count, bits, oi);
                outgoing_bits_count -= count;
            }
            return count;
        }

        private void PushBits(byte count, byte bits, OutputInfo oi)
        {
            if (count == 8 && bit_index == 0x80)
            {
                oi.Buf[oi.OutputSize++] = bits;
            }
            else
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    bool bit = (bits & (1 << i)) != 0;
                    if (bit)
                    {
                        current_byte |= bit_index;
                    }
                    bit_index >>= 1;
                    if (bit_index == 0x00)
                    {
                        bit_index = 0x80;
                        oi.Buf[oi.OutputSize++] = current_byte;
                        current_byte = 0x00;
                    }
                }
            }
        }

        private void PushLiteralByte(OutputInfo oi)
        {
            int processed_offset = match_scan_index - 1;
            int input_offset = GetInputOffset() + processed_offset;
            byte c = buffer[input_offset];
            PushBits(8, c, oi);
        }

        private void SaveBacklog()
        {
            int input_buf_sz = GetInputBufferSize();
            ushort msi = match_scan_index;
            ushort rem = (ushort)(input_buf_sz - msi);
            ushort shift_sz = (ushort)(input_buf_sz + rem);

            // Calculate source index, ensuring it's not negative
            int sourceIndex = input_buf_sz - rem;
            if (sourceIndex < 0)
            {
                sourceIndex = 0;
            }

            // Create a temporary buffer to handle overlapping memory regions
            byte[] tempBuffer = new byte[shift_sz];
            Array.Copy(buffer, sourceIndex, tempBuffer, 0, shift_sz);
            Array.Copy(tempBuffer, 0, buffer, 0, shift_sz);

            // Update state
            match_scan_index = 0;
            input_size = (ushort)(input_size - (input_buf_sz - rem));
        }

        public static byte[] Compress(byte window_sz2, byte lookahead_sz2, byte[] data)
        {
            // Create encoder with specified window size and lookahead size
            HeatshrinkEncoder encoder = new HeatshrinkEncoder(window_sz2, lookahead_sz2);

            // Compress data
            int inputOffset = 0;
            int inputSize = data.Length;
            List<byte> compressed = new List<byte>();

            while (inputOffset < inputSize)
            {
                int bytesToSink = Math.Min(1024, inputSize - inputOffset);
                int bytesSunk = 0;
                byte[] inputBuffer = new byte[bytesToSink];
                Array.Copy(data, inputOffset, inputBuffer, 0, bytesToSink);
                var sinkResult = encoder.Sink(inputBuffer, bytesToSink, ref bytesSunk);
                inputOffset += bytesSunk;

                // Poll for output
                byte[] outputBuffer = new byte[1024];
                int outputSize = 0;
                var pollResult = encoder.Poll(outputBuffer, outputBuffer.Length, ref outputSize);
                do
                {
                    if (outputSize > 0)
                    {
                        compressed.AddRange(outputBuffer.Take(outputSize));
                    }
                    outputSize = 0;
                    pollResult = encoder.Poll(outputBuffer, outputBuffer.Length, ref outputSize);
                } while (pollResult == HSE_poll_res.HSER_POLL_MORE);
                if (outputSize > 0)
                {
                    compressed.AddRange(outputBuffer.Take(outputSize));
                }
            }

            // Finish encoding
            var finishResult = encoder.Finish();
            while (finishResult == HSE_finish_res.HSER_FINISH_MORE)
            {
                byte[] outputBuffer = new byte[1024];
                int outputSize = 0;
                var pollResult = encoder.Poll(outputBuffer, outputBuffer.Length, ref outputSize);
                if (outputSize > 0)
                {
                    compressed.AddRange(outputBuffer.Take(outputSize));
                }
                finishResult = encoder.Finish();
            }

            return compressed.ToArray();
        }
    }
}
