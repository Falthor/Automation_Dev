using UnityEngine;

namespace Game.Presentation
{
    /// <summary>Loops a fixed sequence of sprites on a SpriteRenderer at a constant frame rate.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteFlipbook : MonoBehaviour
    {
        SpriteRenderer _renderer;
        Sprite[] _frames;
        float _secondsPerFrame;
        float _timer;
        int _index;

        public void Initialize(Sprite[] frames, float fps)
        {
            _renderer = GetComponent<SpriteRenderer>();
            _frames = frames;
            _secondsPerFrame = fps > 0f ? 1f / fps : 0f;
            _timer = 0f;
            _index = 0;
        }

        void Update()
        {
            if (_frames == null || _frames.Length < 2 || _secondsPerFrame <= 0f) return;

            _timer += Time.deltaTime;
            while (_timer >= _secondsPerFrame)
            {
                _timer -= _secondsPerFrame;
                _index = (_index + 1) % _frames.Length;
            }

            _renderer.sprite = _frames[_index];
        }
    }
}
