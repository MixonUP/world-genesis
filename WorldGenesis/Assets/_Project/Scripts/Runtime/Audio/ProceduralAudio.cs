using System;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    /// <summary>Original procedural candidates. Presentation only; never touches simulation RNG.</summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralAudio : MonoBehaviour
    {
        private const int VoiceCount = 4;
        private const string MutePreference = "WorldGenesis.AudioMuted";
        [SerializeField, Range(0, .2f)] private float masterVolume = .12f;
        private readonly AudioSource[] voices = new AudioSource[VoiceCount];
        private readonly float[] voiceEnds = new float[VoiceCount];
        private readonly float[] nextCueTimes = new float[12];
        private readonly AudioClip[,] clips = new AudioClip[12, 2];
        private AudioSource ambience;
        private AudioClip ambientClip;
        private GameBootstrap game;
        private WorldState observedWorld;
        private int lastEventId;
        private bool muted;
        public bool Muted { get { return muted; } set { SetMuted(value); } }

        private void Awake()
        {
            game = GetComponent<GameBootstrap>();
            muted = PlayerPrefs.GetInt(MutePreference, 0) != 0;
            for (int i = 0; i < 12; i++)
            {
                if (i == (int)EventKind.Death) continue;
                for (int variation = 0; variation < 2; variation++)
                {
                    float[] pcm = ProceduralSound.RenderCue((EventKind)i, variation);
                    AudioClip clip = AudioClip.Create("WG cue " + (EventKind)i + " v" + variation, pcm.Length, 1, ProceduralSound.SampleRate, false);
                    clip.SetData(pcm, 0); clips[i, variation] = clip;
                }
            }
            for (int i = 0; i < VoiceCount; i++) voices[i] = CreateSource("Event voice " + i);
            float[] ambient = ProceduralSound.RenderAmbient();
            ambientClip = AudioClip.Create("WG quiet wind and glass • 8 seconds", ambient.Length, 1, ProceduralSound.SampleRate, false);
            ambientClip.SetData(ambient, 0);
            ambience = CreateSource("World ambience"); ambience.clip = ambientClip; ambience.loop = true;
            ambience.volume = 0; ambience.Play();
            ApplyMute();
        }

        private AudioSource CreateSource(string sourceName)
        {
            GameObject child = new GameObject(sourceName); child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.dopplerLevel = 0;
            source.volume = 0; source.priority = 180; return source;
        }

        private void Update()
        {
            if (game == null || game.Engine == null) return;
            WorldState world = game.World;
            if (!ReferenceEquals(world, observedWorld))
            {
                observedWorld = world; lastEventId = 0;
                for (int i = 0; i < world.Events.Count; i++) lastEventId = Math.Max(lastEventId, world.Events[i].Id);
                for (int i = 0; i < VoiceCount; i++) { voices[i].Stop(); voiceEnds[i] = 0; }
                for (int i = 0; i < nextCueTimes.Length; i++) nextCueTimes[i] = 0;
            }
            float now = Time.unscaledTime;
            ambience.volume = Mathf.MoveTowards(ambience.volume, masterVolume * (game.Paused ? .08f : .16f), Time.unscaledDeltaTime * .025f);
            for (int i = 0; i < world.Events.Count; i++)
            {
                WorldEvent item = world.Events[i];
                if (item.Id <= lastEventId) continue;
                lastEventId = item.Id;
                if (!muted) PlayEvent(item, now);
            }
        }

        private void PlayEvent(WorldEvent item, float now)
        {
            int kind = (int)item.Kind;
            if (kind < 0 || kind >= 12 || clips[kind, 0] == null || now < nextCueTimes[kind]) return;
            bool impact = item.Kind == EventKind.Meteor || item.Kind == EventKind.Lightning;
            bool town = item.Kind == EventKind.Built || item.Kind == EventKind.Birth || item.Kind == EventKind.Founded;
            int priority = impact ? 3 : town ? 1 : 2;
            int selected = -1;
            for (int i = 0; i < VoiceCount; i++)
                if (now >= voiceEnds[i]) { selected = i; break; }
            // Never restart a busy voice: dense event streams are coalesced, avoiding cut tails/clicks.
            if (selected < 0) return;
            int variant = (item.Id & 1);
            AudioClip clip = clips[kind, variant];
            AudioSource source = voices[selected];
            source.clip = clip;
            source.volume = masterVolume * (town ? .45f : impact ? 1f : .75f);
            source.priority = 200 - priority * 30;
            source.panStereo = Mathf.Clamp((item.X - game.MainCamera.transform.position.x) / Mathf.Max(1, game.MainCamera.orthographicSize * game.MainCamera.aspect), -.55f, .55f);
            source.pitch = 1; source.Play();
            voiceEnds[selected] = now + clip.length;
            nextCueTimes[kind] = now + (town ? 1.3f : .18f);
        }

        public void ToggleMuted() { SetMuted(!muted); }
        private void SetMuted(bool value)
        {
            if (muted == value) return;
            muted = value; PlayerPrefs.SetInt(MutePreference, value ? 1 : 0); PlayerPrefs.Save(); ApplyMute();
            // Muted events are consumed in Update and never replayed when sound returns.
        }
        private void ApplyMute()
        {
            for (int i = 0; i < VoiceCount; i++) if (voices[i] != null) voices[i].mute = muted;
            if (ambience != null) ambience.mute = muted;
        }
        private void OnDisable()
        {
            for (int i = 0; i < VoiceCount; i++) if (voices[i] != null) voices[i].Stop();
            if (ambience != null) ambience.Stop();
        }
        private void OnEnable() { observedWorld = null; if (ambience != null && !ambience.isPlaying) ambience.Play(); }
        private void OnDestroy()
        {
            for (int i = 0; i < VoiceCount; i++) if (voices[i] != null) Destroy(voices[i].gameObject);
            if (ambience != null) Destroy(ambience.gameObject);
            for (int i = 0; i < 12; i++) for (int v = 0; v < 2; v++) if (clips[i, v] != null) Destroy(clips[i, v]);
            if (ambientClip != null) Destroy(ambientClip);
        }
    }

    /// <summary>Reproducible mono PCM recipes; amplitudes include headroom, not perceptual mastering.</summary>
    public static class ProceduralSound
    {
        public const int SampleRate = 22050;
        private const double Tau = Math.PI * 2;

        public static float[] RenderCue(EventKind kind, int variant = 0)
        {
            double duration = kind == EventKind.Meteor ? 1.5 : kind == EventKind.Lightning ? 1.15 : kind == EventKind.Rain ? 1.3 : kind == EventKind.Bless ? 1.0 : .72;
            int count = (int)(duration * SampleRate);
            float[] samples = new float[count];
            uint noiseState = unchecked(0x9156AB3Du + (uint)kind * 719u + (uint)variant * 3571u);
            double lowNoise = 0, peak = 0;
            double tuning = variant == 0 ? 1 : 1.025;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
                double noise = ((noiseState >> 8) / 8388608.0) - 1;
                lowNoise += .065 * (noise - lowNoise);
                double body;
                switch (kind)
                {
                    case EventKind.Fire:
                        body = (lowNoise * .9 + noise * .055 * Math.Pow(.5 + .5 * Math.Sin(Tau * 23 * t), 14)) * Math.Exp(-t * 3); break;
                    case EventKind.Rain:
                        body = (noise - lowNoise) * .075 * (.75 + .25 * Math.Sin(Tau * 2 * t)) + Bell(t - .12, 1500 * tuning) * .022; break;
                    case EventKind.Lightning:
                        body = noise * .22 * Math.Exp(-t * 24) + lowNoise * .9 * Math.Exp(-t * 3) + Math.Sin(Tau * (82 * t - 24 * t * t)) * .20 * Math.Exp(-t * 5); break;
                    case EventKind.Meteor:
                        body = Math.Sin(Tau * (70 * t - 13 * t * t)) * .34 * Math.Exp(-t * 3.5) + lowNoise * 1.3 * Math.Exp(-t * 2.7) + noise * .055 * Math.Exp(-t * 18); break;
                    case EventKind.Heal:
                        body = .20 * Bell(t, 523.25 * tuning) + .17 * Bell(t - .11, 659.25 * tuning) + .13 * Bell(t - .22, 783.99 * tuning); break;
                    case EventKind.Bless:
                        body = .16 * Bell(t, 392 * tuning) + .16 * Bell(t - .13, 587.33 * tuning) + .14 * Bell(t - .26, 783.99 * tuning); break;
                    case EventKind.Plague:
                        body = .12 * Math.Sin(Tau * (215 * t - 25 * t * t)) * Math.Exp(-t * 4) + lowNoise * .10; break;
                    case EventKind.Birth:
                        body = .15 * Bell(t, 659.25 * tuning) + .13 * Bell(t - .12, 880 * tuning); break;
                    case EventKind.Built:
                        body = .16 * Math.Sin(Tau * 180 * tuning * t) * Math.Exp(-t * 24) + .075 * noise * Math.Exp(-t * 55) + .09 * Bell(t - .09, 440 * tuning); break;
                    case EventKind.Founded:
                        body = .14 * Bell(t, 293.66 * tuning) + .12 * Bell(t - .12, 440 * tuning) + .10 * Bell(t - .24, 587.33 * tuning); break;
                    case EventKind.War:
                        body = (.12 * Math.Sin(Tau * 146.83 * tuning * t) + .07 * Math.Sin(Tau * 220 * tuning * t)) * Math.Exp(-t * 4); break;
                    default: body = 0; break;
                }
                double attack = Math.Min(1, t / .008);
                double release = Math.Min(1, (duration - t) / .11);
                double sample = body * attack * release * release;
                samples[i] = (float)sample; peak = Math.Max(peak, Math.Abs(sample));
            }
            float gain = peak > .55 ? (float)(.55 / peak) : 1;
            for (int i = 0; i < count; i++) samples[i] *= gain;
            samples[0] = 0; samples[count - 1] = 0;
            return samples;
        }

        private static double Bell(double time, double frequency)
        {
            if (time < 0) return 0;
            double envelope = Math.Min(1, time / .012) * Math.Exp(-time * 7);
            return envelope * (Math.Sin(Tau * frequency * time) + .23 * Math.Sin(Tau * frequency * 2.01 * time) * Math.Exp(-time * 5));
        }

        public static float[] RenderAmbient()
        {
            float[] samples = new float[SampleRate * 8];
            // Integer periods and modulation cycles across 8 s give a periodic loop without a random seam.
            for (int i = 0; i < samples.Length; i++)
            {
                double t = (double)i / SampleRate;
                double breath = .65 + .25 * Math.Sin(Tau * t / 8) + .10 * Math.Sin(Tau * t / 4);
                double wind = 0;
                for (int harmonic = 0; harmonic < 12; harmonic++)
                    wind += Math.Sin(Tau * (417 + harmonic * 173) / 8.0 * t + harmonic * .71) * .018;
                double glass = Math.Sin(Tau * 1175 / 8.0 * t) * .037 + Math.Sin(Tau * 1760 / 8.0 * t) * .025 + Math.Sin(Tau * 2349 / 8.0 * t) * .013;
                samples[i] = (float)((wind + glass) * breath);
            }
            return samples;
        }
    }
}
