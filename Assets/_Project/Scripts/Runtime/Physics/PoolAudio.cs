using System.Collections.Generic;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **音频层**：把内核的接触事件流（<see cref="PoolSimulator.DrainContactEvents"/>）映射成"撞声"，
    /// 并把出杆（<see cref="PhysicsTestDrive.LaunchCount"/> 的计数增量）映射成 strike。
    ///
    /// ── 分层（与瞄准辅助同范式）────────────────────────────────────────
    /// · **纯逻辑**：<see cref="VolumeForSpeed"/> / <see cref="PitchForSerial"/> / <see cref="ThrottleState"/>
    ///   —— 判据 `T90` 直接驱动；
    /// · 本类 <c>Update</c>：薄胶水 —— 轮询出杆计数、drain 接触事件、映射 + 限流 + <c>PlayOneShot</c>。
    ///
    /// ── 三条契约 ─────────────────────────────────────────────────────
    ///  1. **关闭即静默且不积压**：<see cref="audioEnabled"/> 为假 ⇒ 不播放，且**仍 drain 并丢弃**积压事件
    ///     （否则重新开启瞬间会炸一耳朵旧事件）；
    ///  2. **strike 每帧至多一次**（计数增量天然有界）；
    ///  3. **限流**：同帧同类 ≤ 3 + 每新帧首个接受须距上次 ≥ 0.03 s（防开球"机关枪"）；
    ///     音高按播放序号三值轮转（**确定性**抖动，不用随机数）。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────
    /// 真实听感（清脆 / 闷 / 轰炸）不被 EditMode 判据覆盖（批处理无声）——排期验收由用户实机。
    /// </summary>
    [AddComponentMenu("Billiards/Pool Audio")]
    public sealed class PoolAudio : MonoBehaviour
    {
        /// <summary>同帧同类事件最多播放数（开球群碰撞防叠）。</summary>
        public const int MaxSameKindPerFrame = 3;

        /// <summary>同类事件跨帧最小播放间隔（秒）。</summary>
        public const float MinRepeatIntervalSeconds = 0.03f;

        /// <summary>音量调制满档对应的相对速度（m/s，= 出厂初速上界）。</summary>
        public const float FullSpeedForVolume = 4.5f;

        /// <summary>AudioSource 池大小（限流后同时发声数有界）。</summary>
        public const int SourcePoolSize = 6;

        [Tooltip("被观察的驱动器（出杆计数 + 内核）。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        [Tooltip("出杆音（strike）。")]
        public AudioClip strikeClip;

        [Tooltip("球-球撞音（hit）。")]
        public AudioClip hitClip;

        [Tooltip("球-库撞音（rail）。")]
        public AudioClip railClip;

        [Tooltip("落袋音（pocket）。")]
        public AudioClip pocketClip;

        [Tooltip("背景音乐（循环播放，音量取主音量 × 0.45）。")]
        public AudioClip bgmClip;

        /// <summary>BGM 相对主音量的比例（背景不抢前景撞声）。</summary>
        public const float BgmVolumeRatio = 0.45f;

        /// <summary>音频总开关（设置页预留接口：单字段单读点；关闭 ⇒ 不播放且丢弃积压事件）。</summary>
        [Tooltip("音频总开关。")]
        public bool audioEnabled = true;

        /// <summary>主音量（设置页预留接口）。</summary>
        [Range(0f, 1f)]
        [Tooltip("主音量。")]
        public float masterVolume = 0.8f;

        private readonly List<ContactEvent> _events = new List<ContactEvent>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private readonly ThrottleState _throttle = new ThrottleState();
        private AudioSource _bgmSource;
        private int _lastLaunchCount;
        private int _playSerial;
        private int _sourceCursor;

        private void Start()
        {
            if (bgmClip == null)
            {
                return;
            }

            var go = new GameObject("PoolAudioBgm");
            go.transform.SetParent(transform, false);
            _bgmSource = go.AddComponent<AudioSource>();
            _bgmSource.clip = bgmClip;
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;
            _bgmSource.spatialBlend = 0f;
            _bgmSource.volume = Mathf.Clamp01(BgmVolumeRatio * masterVolume);
            _bgmSource.Play();
        }

        // ── 纯逻辑（判据 T90 直接驱动）──────────────────────────────────────

        /// <summary>相对速度 → 音量 ∈ [0.25, 1]（1:1 线性；轻碰轻响，快撞满响）。</summary>
        public static float VolumeForSpeed(float relativeSpeed, float fullSpeed = FullSpeedForVolume)
        {
            if (!(fullSpeed > 0f))
            {
                return 0.25f;
            }

            return Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(relativeSpeed / fullSpeed));
        }

        /// <summary>播放序号 → 音高（0.94 / 1.00 / 1.06 三值轮转——**确定性抖动**，不用随机数）。</summary>
        public static float PitchForSerial(int serial)
        {
            int phase = ((serial % 3) + 3) % 3;
            return 1f + 0.06f * (phase - 1);
        }

        /// <summary>
        /// **同帧多碰撞限流器**（纯逻辑，判据 `T90` 驱动）：同帧同类 ≤ <see cref="MaxSameKindPerFrame"/>；
        /// **每个新帧的首个**接受还须距上次接受 ≥ <see cref="MinRepeatIntervalSeconds"/>（同帧内豁免间隔检查，
        /// 否则"同帧允许 3 个"会被间隔规则当场否决）。帧边界按调用方传入的 <paramref name="nowSeconds"/>
        /// 变化判定（不读 `Time` ⇒ 可判据直接驱动）。
        /// </summary>
        public sealed class ThrottleState
        {
            private readonly int[] _acceptedThisFrame = new int[3];
            private readonly float[] _lastAcceptedAt = new float[3];
            private float _frameStamp = float.NaN;

            public ThrottleState()
            {
                Reset();
            }

            public bool TryAccept(ContactKind kind, float nowSeconds)
            {
                bool newFrame = float.IsNaN(_frameStamp) || nowSeconds != _frameStamp;
                if (newFrame)
                {
                    _frameStamp = nowSeconds;
                    for (int i = 0; i < _acceptedThisFrame.Length; i++)
                    {
                        _acceptedThisFrame[i] = 0;
                    }
                }

                int k = (int)kind;
                if (_acceptedThisFrame[k] >= MaxSameKindPerFrame)
                {
                    return false;
                }

                if (newFrame
                    && !float.IsNaN(_lastAcceptedAt[k])
                    && nowSeconds - _lastAcceptedAt[k] < MinRepeatIntervalSeconds)
                {
                    return false;
                }

                _acceptedThisFrame[k]++;
                _lastAcceptedAt[k] = nowSeconds;
                return true;
            }

            public void Reset()
            {
                _frameStamp = float.NaN;
                for (int i = 0; i < _acceptedThisFrame.Length; i++)
                {
                    _acceptedThisFrame[i] = 0;
                    _lastAcceptedAt[i] = float.NaN;
                }
            }
        }

        // ── 胶水（薄；输入/扬声器不被判据覆盖）────────────────────────────────

        private void Update()
        {
            if (drive == null)
            {
                return;
            }

            PoolSimulator sim = drive.Simulator;

            if (!audioEnabled)
            {
                // 关闭 ⇒ 不播放；同时丢弃积压（否则开启瞬间炸一耳朵旧事件），BGM 静音。
                if (_bgmSource != null && !_bgmSource.mute)
                {
                    _bgmSource.mute = true;
                }

                _events.Clear();
                sim?.DrainContactEvents(_events);
                return;
            }

            if (_bgmSource != null && _bgmSource.mute)
            {
                _bgmSource.mute = false;
            }

            float now = Time.unscaledTime;

            // 1) 出杆音（轮询计数增量；每帧至多一次）
            if (drive.LaunchCount != _lastLaunchCount)
            {
                _lastLaunchCount = drive.LaunchCount;
                Play(strikeClip, VolumeForSpeed(drive.LastLaunchSpeed));
            }

            // 2) 接触事件流：drain → 限流筛选 → 播放
            if (sim == null)
            {
                return;
            }

            _events.Clear();
            if (sim.DrainContactEvents(_events) == 0)
            {
                return;
            }

            for (int i = 0; i < _events.Count; i++)
            {
                ContactEvent contact = _events[i];
                if (!_throttle.TryAccept(contact.kind, now))
                {
                    continue;
                }

                Play(ClipFor(contact.kind), VolumeForSpeed(contact.relativeSpeed));
            }
        }

        private AudioClip ClipFor(ContactKind kind)
        {
            switch (kind)
            {
                case ContactKind.BallBall:
                    return hitClip;
                case ContactKind.Cushion:
                    return railClip;
                case ContactKind.Pocket:
                    return pocketClip;
                default:
                    return null;
            }
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null)
            {
                return;
            }

            AudioSource source = NextSource();
            source.pitch = PitchForSerial(_playSerial++);
            source.PlayOneShot(clip, Mathf.Clamp01(volume * masterVolume));
        }

        private AudioSource NextSource()
        {
            while (_sources.Count < SourcePoolSize)
            {
                var go = new GameObject("PoolAudioSource_" + _sources.Count);
                go.transform.SetParent(transform, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _sources.Add(source);
            }

            AudioSource chosen = _sources[_sourceCursor];
            _sourceCursor = (_sourceCursor + 1) % _sources.Count;
            return chosen;
        }
    }
}
