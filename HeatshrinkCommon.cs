namespace HeatshrinkCSharp
{
    public static class HeatshrinkCommon
    {
        public const uint HEATSHRINK_MIN_WINDOW_BITS = 4;
        public const uint HEATSHRINK_MAX_WINDOW_BITS = 15;
        public const uint HEATSHRINK_MIN_LOOKAHEAD_BITS = 3;
        public const uint HEATSHRINK_LITERAL_MARKER = 1;
        public const uint HEATSHRINK_BACKREF_MARKER = 0;
        public const ushort MATCH_NOT_FOUND = 0xFFFF;
        public const ushort NO_BITS = 0xFFFF;
    }
}
