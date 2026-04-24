namespace HeatshrinkCSharp
{
    public enum HSD_sink_res
    {
        HSDR_SINK_OK = 0,               /* data sunk, ready to poll */
        HSDR_SINK_FULL = 1,             /* out of space in internal buffer */
        HSDR_SINK_ERROR_NULL = -1,      /* NULL argument */
    }

    public enum HSD_poll_res
    {
        HSDR_POLL_EMPTY = 0,            /* input exhausted */
        HSDR_POLL_MORE = 1,             /* more data remaining, call again w/ fresh output buffer */
        HSDR_POLL_ERROR_NULL = -1,      /* NULL arguments */
        HSDR_POLL_ERROR_UNKNOWN = -2,
    }

    public enum HSD_finish_res
    {
        HSDR_FINISH_DONE = 0,           /* output is done */
        HSDR_FINISH_MORE = 1,           /* more output remains */
        HSDR_FINISH_ERROR_NULL = -1,    /* NULL arguments */
    }

    internal enum HSD_state
    {
        HSDS_TAG_BIT,                   /* tag bit */
        HSDS_YIELD_LITERAL,             /* ready to yield literal byte */
        HSDS_BACKREF_INDEX_MSB,         /* most significant byte of index */
        HSDS_BACKREF_INDEX_LSB,         /* least significant byte of index */
        HSDS_BACKREF_COUNT_MSB,         /* most significant byte of count */
        HSDS_BACKREF_COUNT_LSB,         /* least significant byte of count */
        HSDS_YIELD_BACKREF,             /* ready to yield back-reference */
    }

    public class HeatshrinkDecoder
    {
        private ushort input_size;      /* bytes in input buffer */
        private ushort input_index;     /* offset to next unprocessed input byte */
        private ushort output_count;    /* how many bytes to output */
        private ushort output_index;    /* index for bytes to output */
        private ushort head_index;      /* head of window buffer */
        private HSD_state state;        /* current state machine node */
        private byte current_byte;      /* current byte of input */
        private byte bit_index;         /* current bit index */
        private byte window_sz2;        /* window buffer bits */
        private byte lookahead_sz2;     /* lookahead bits */
        private ushort input_buffer_size; /* input buffer size */
        private byte[] buffers;         /* Input buffer, then expansion window buffer */

        public HeatshrinkDecoder(ushort input_buffer_size, byte window_sz2, byte lookahead_sz2)
        {
            if (window_sz2 < HeatshrinkCommon.HEATSHRINK_MIN_WINDOW_BITS ||
                window_sz2 > HeatshrinkCommon.HEATSHRINK_MAX_WINDOW_BITS ||
                input_buffer_size == 0 ||
                lookahead_sz2 < HeatshrinkCommon.HEATSHRINK_MIN_LOOKAHEAD_BITS ||
                lookahead_sz2 >= window_sz2)
            {
                throw new ArgumentException("Invalid input buffer size, window or lookahead size");
            }

            this.input_buffer_size = input_buffer_size;
            this.window_sz2 = window_sz2;
            this.lookahead_sz2 = lookahead_sz2;
            int buffers_sz = (1 << window_sz2) + input_buffer_size;
            this.buffers = new byte[buffers_sz];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(buffers, 0, buffers.Length);
            state = HSD_state.HSDS_TAG_BIT;
            input_size = 0;
            input_index = 0;
            bit_index = 0x00;
            current_byte = 0x00;
            output_count = 0;
            output_index = 0;
            head_index = 0;
        }

        public HSD_sink_res Sink(byte[] in_buf, int size, ref int input_size_out)
        {
            if (in_buf == null)
            {
                return HSD_sink_res.HSDR_SINK_ERROR_NULL;
            }

            int rem = input_buffer_size - input_size;
            if (rem == 0)
            {
                input_size_out = 0;
                return HSD_sink_res.HSDR_SINK_FULL;
            }

            size = rem < size ? rem : size;
            Array.Copy(in_buf, 0, buffers, input_size, size);
            input_size += (ushort)size;
            input_size_out = size;
            return HSD_sink_res.HSDR_SINK_OK;
        }

        public HSD_poll_res Poll(byte[] out_buf, int out_buf_size, ref int output_size)
        {
            if (out_buf == null)
            {
                return HSD_poll_res.HSDR_POLL_ERROR_NULL;
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
                HSD_state in_state = state;
                switch (in_state)
                {
                    case HSD_state.HSDS_TAG_BIT:
                        state = StTagBit();
                        break;
                    case HSD_state.HSDS_YIELD_LITERAL:
                        state = StYieldLiteral(oi);
                        break;
                    case HSD_state.HSDS_BACKREF_INDEX_MSB:
                        state = StBackrefIndexMsb();
                        break;
                    case HSD_state.HSDS_BACKREF_INDEX_LSB:
                        state = StBackrefIndexLsb();
                        break;
                    case HSD_state.HSDS_BACKREF_COUNT_MSB:
                        state = StBackrefCountMsb();
                        break;
                    case HSD_state.HSDS_BACKREF_COUNT_LSB:
                        state = StBackrefCountLsb();
                        break;
                    case HSD_state.HSDS_YIELD_BACKREF:
                        state = StYieldBackref(oi);
                        break;
                    default:
                        return HSD_poll_res.HSDR_POLL_ERROR_UNKNOWN;
                }

                if (state == in_state)
                {
                    if (oi.OutputSize == out_buf_size)
                    {
                        output_size = oi.OutputSize;
                        return HSD_poll_res.HSDR_POLL_MORE;
                    }
                    output_size = oi.OutputSize;
                    return HSD_poll_res.HSDR_POLL_EMPTY;
                }
            }
        }

        public HSD_finish_res Finish()
        {
            switch (state)
            {
                case HSD_state.HSDS_TAG_BIT:
                    return input_size == 0 ? HSD_finish_res.HSDR_FINISH_DONE : HSD_finish_res.HSDR_FINISH_MORE;
                case HSD_state.HSDS_BACKREF_INDEX_LSB:
                case HSD_state.HSDS_BACKREF_INDEX_MSB:
                case HSD_state.HSDS_BACKREF_COUNT_LSB:
                case HSD_state.HSDS_BACKREF_COUNT_MSB:
                    return input_size == 0 ? HSD_finish_res.HSDR_FINISH_DONE : HSD_finish_res.HSDR_FINISH_MORE;
                case HSD_state.HSDS_YIELD_LITERAL:
                    return input_size == 0 ? HSD_finish_res.HSDR_FINISH_DONE : HSD_finish_res.HSDR_FINISH_MORE;
                default:
                    return HSD_finish_res.HSDR_FINISH_MORE;
            }
        }

        private HSD_state StTagBit()
        {
            ushort bits = GetBits(1);
            if (bits == HeatshrinkCommon.NO_BITS)
            {
                return HSD_state.HSDS_TAG_BIT;
            }
            else if (bits != 0)
            {
                return HSD_state.HSDS_YIELD_LITERAL;
            }
            else if (window_sz2 > 8)
            {
                return HSD_state.HSDS_BACKREF_INDEX_MSB;
            }
            else
            {
                output_index = 0;
                return HSD_state.HSDS_BACKREF_INDEX_LSB;
            }
        }

        private HSD_state StYieldLiteral(OutputInfo oi)
        {
            if (oi.OutputSize < oi.BufSize)
            {
                ushort byte_val = GetBits(8);
                if (byte_val == HeatshrinkCommon.NO_BITS)
                {
                    return HSD_state.HSDS_YIELD_LITERAL;
                }

                byte[] buf = buffers;
                int buf_offset = input_buffer_size;
                ushort mask = (ushort)((1 << window_sz2) - 1);
                byte c = (byte)(byte_val & 0xFF);
                buf[buf_offset + (head_index++ & mask)] = c;
                PushByte(oi, c);
                return HSD_state.HSDS_TAG_BIT;
            }
            else
            {
                return HSD_state.HSDS_YIELD_LITERAL;
            }
        }

        private HSD_state StBackrefIndexMsb()
        {
            byte bit_ct = window_sz2;
            ushort bits = GetBits((byte)(bit_ct - 8));
            if (bits == HeatshrinkCommon.NO_BITS)
            {
                return HSD_state.HSDS_BACKREF_INDEX_MSB;
            }
            output_index = (ushort)(bits << 8);
            return HSD_state.HSDS_BACKREF_INDEX_LSB;
        }

        private HSD_state StBackrefIndexLsb()
        {
            byte bit_ct = window_sz2;
            ushort bits = GetBits((byte)(bit_ct < 8 ? bit_ct : 8));
            if (bits == HeatshrinkCommon.NO_BITS)
            {
                return HSD_state.HSDS_BACKREF_INDEX_LSB;
            }
            output_index |= bits;
            output_index++;
            byte br_bit_ct = lookahead_sz2;
            output_count = 0;
            return (br_bit_ct > 8) ? HSD_state.HSDS_BACKREF_COUNT_MSB : HSD_state.HSDS_BACKREF_COUNT_LSB;
        }

        private HSD_state StBackrefCountMsb()
        {
            byte br_bit_ct = lookahead_sz2;
            ushort bits = GetBits((byte)(br_bit_ct - 8));
            if (bits == HeatshrinkCommon.NO_BITS)
            {
                return HSD_state.HSDS_BACKREF_COUNT_MSB;
            }
            output_count = (ushort)(bits << 8);
            return HSD_state.HSDS_BACKREF_COUNT_LSB;
        }

        private HSD_state StBackrefCountLsb()
        {
            byte br_bit_ct = lookahead_sz2;
            ushort bits = GetBits((byte)(br_bit_ct < 8 ? br_bit_ct : 8));
            if (bits == HeatshrinkCommon.NO_BITS)
            {
                return HSD_state.HSDS_BACKREF_COUNT_LSB;
            }
            output_count |= bits;
            output_count++;
            return HSD_state.HSDS_YIELD_BACKREF;
        }

        private HSD_state StYieldBackref(OutputInfo oi)
        {
            int count = oi.BufSize - oi.OutputSize;
            if (count > 0)
            {
                if (output_count < count)
                {
                    count = output_count;
                }

                byte[] buf = buffers;
                int buf_offset = input_buffer_size;
                ushort mask = (ushort)((1 << window_sz2) - 1);
                ushort neg_offset = output_index;

                for (int i = 0; i < count; i++)
                {
                    byte c = buf[buf_offset + ((head_index - neg_offset) & mask)];
                    PushByte(oi, c);
                    buf[buf_offset + (head_index & mask)] = c;
                    head_index++;
                }

                output_count -= (ushort)count;
                if (output_count == 0)
                {
                    return HSD_state.HSDS_TAG_BIT;
                }
            }
            return HSD_state.HSDS_YIELD_BACKREF;
        }

        private ushort GetBits(byte count)
        {
            ushort accumulator = 0;
            if (count > 15)
            {
                return HeatshrinkCommon.NO_BITS;
            }

            if (input_size == 0)
            {
                if (bit_index < (1 << (count - 1)))
                {
                    return HeatshrinkCommon.NO_BITS;
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (bit_index == 0x00)
                {
                    if (input_size == 0)
                    {
                        return HeatshrinkCommon.NO_BITS;
                    }
                    current_byte = buffers[input_index++];
                    if (input_index == input_size)
                    {
                        input_index = 0;
                        input_size = 0;
                    }
                    bit_index = 0x80;
                }
                accumulator <<= 1;
                if ((current_byte & bit_index) != 0)
                {
                    accumulator |= 0x01;
                }
                bit_index >>= 1;
            }

            return accumulator;
        }

        private void PushByte(OutputInfo oi, byte byte_val)
        {
            oi.Buf[oi.OutputSize++] = byte_val;
        }

        public static byte[] Decompress(byte window_sz2, byte lookahead_sz2, byte[] data)
        {
            // Create decoder with input buffer size 1024, specified window size and lookahead size
            HeatshrinkDecoder decoder = new HeatshrinkDecoder(1024, window_sz2, lookahead_sz2);

            // Decompress data
            int inputOffset = 0;
            int inputSize = data.Length;
            List<byte> decompressed = new List<byte>();

            while (inputOffset < inputSize)
            {
                int bytesToSink = Math.Min(1024, inputSize - inputOffset);
                int bytesSunk = 0;
                byte[] inputBuffer = new byte[bytesToSink];
                Array.Copy(data, inputOffset, inputBuffer, 0, bytesToSink);
                var sinkResult = decoder.Sink(inputBuffer, bytesToSink, ref bytesSunk);
                inputOffset += bytesSunk;

                // Poll for output
                byte[] outputBuffer = new byte[1024];
                int outputSize = 0;
                var pollResult = decoder.Poll(outputBuffer, outputBuffer.Length, ref outputSize);
                do
                {
                    if (outputSize > 0)
                    {
                        decompressed.AddRange(outputBuffer.Take(outputSize));
                    }
                    outputSize = 0;
                    pollResult = decoder.Poll(outputBuffer, outputBuffer.Length, ref outputSize);
                } while (pollResult == HSD_poll_res.HSDR_POLL_MORE);
                if (outputSize > 0)
                {
                    decompressed.AddRange(outputBuffer.Take(outputSize));
                }
            }

            // Finish decoding
            var finishResult = decoder.Finish();
            while (finishResult == HSD_finish_res.HSDR_FINISH_MORE)
            {
                byte[] outputBuffer = new byte[1024];
                int outputSize = 0;
                var pollResult = decoder.Poll(outputBuffer, outputBuffer.Length, ref outputSize);
                if (outputSize > 0)
                {
                    decompressed.AddRange(outputBuffer.Take(outputSize));
                }
                finishResult = decoder.Finish();
            }

            return decompressed.ToArray();
        }
    }
}
