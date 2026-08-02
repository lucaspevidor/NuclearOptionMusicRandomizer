namespace MusicRandomizer
{
    internal static class TakeoffMusicContext
    {
        private static int _depth;

        public static bool IsActive => _depth > 0;

        public static void Enter()
        {
            _depth++;
        }

        public static void Exit()
        {
            if (_depth > 0)
            {
                _depth--;
            }
        }
    }
}
