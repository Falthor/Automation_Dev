using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Plays a fixed sequence of sprites on a SpriteRenderer at a constant frame rate, either as a
    /// continuous loop or as a single pass repeated on a fixed interval.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteFlipbook : MonoBehaviour
    {
        SpriteRenderer _renderer;
        Sprite[] _frames;
        float _secondsPerFrame;
        float _cycleIntervalSeconds;
        float _timer;
        int _index;

        /// <summary>
        /// <paramref name="cycleIntervalSeconds"/> zero loops the frames end to end, which is what
        /// every caller did before this parameter existed. A positive value plays the sequence once
        /// every that many seconds and holds the first frame in between - for an animation that
        /// shows something happening rather than something being the case.
        ///
        /// An interval shorter than the sequence itself is raised to the sequence's own length, so
        /// the animation is never cut off partway; it just runs back to back.
        /// </summary>
        public void Initialize(Sprite[] frames, float fps, float cycleIntervalSeconds = 0f)
        {
            _renderer = GetComponent<SpriteRenderer>();
            _frames = frames;
            _secondsPerFrame = fps > 0f ? 1f / fps : 0f;
            _cycleIntervalSeconds = cycleIntervalSeconds;
            _timer = 0f;
            _index = 0;
        }

        void Update()
        {
            if (_frames == null || _frames.Length < 2 || _secondsPerFrame <= 0f) return;

            _timer += Time.deltaTime;

            if (_cycleIntervalSeconds <= 0f)
            {
                while (_timer >= _secondsPerFrame)
                {
                    _timer -= _secondsPerFrame;
                    _index = (_index + 1) % _frames.Length;
                }

                _renderer.sprite = _frames[_index];
                return;
            }

            // Phase within the period, rather than a counter advanced frame by frame: the resting
            // stretch is far longer than the playing one, and accumulating a remainder across it
            // would let rounding drift the start of each pass.
            float playingDuration = _frames.Length * _secondsPerFrame;
            float period = Mathf.Max(_cycleIntervalSeconds, playingDuration);
            if (_timer >= period) _timer -= period;

            _index = _timer < playingDuration
                ? Mathf.Min((int)(_timer / _secondsPerFrame), _frames.Length - 1)
                : 0;
            _renderer.sprite = _frames[_index];
        }
    }
}
