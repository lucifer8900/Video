namespace Lingmai.RedMist
{
    public sealed class StoryThreadChapterLifetime
    {
        private long _generation;

        public long Begin()
        {
            return Advance();
        }

        public void End()
        {
            Advance();
        }

        public bool IsCurrent(long token)
        {
            return token != 0 && token == _generation;
        }

        private long Advance()
        {
            unchecked
            {
                _generation++;
                if (_generation == 0) _generation++;
                return _generation;
            }
        }
    }
}
