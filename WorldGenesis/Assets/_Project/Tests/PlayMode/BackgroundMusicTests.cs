using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldGenesis.Runtime;

namespace WorldGenesis.Tests
{
    public sealed class BackgroundMusicTests
    {
        private GameObject owner;
        private readonly List<AudioClip> created = new List<AudioClip>();
        private float previousScale;
        private int previousMute;
        [SetUp] public void Setup()
        {
            previousScale = Time.timeScale;
            previousMute = PlayerPrefs.GetInt("WorldGenesis.AudioMuted", 0);
            owner = new GameObject("Music test owner");
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale = previousScale;
            PlayerPrefs.SetInt("WorldGenesis.AudioMuted", previousMute);
            if (owner != null) UnityEngine.Object.Destroy(owner);
            yield return null;
            foreach (AudioClip clip in created) if (clip != null) UnityEngine.Object.Destroy(clip);
            created.Clear();
            yield return null;
        }
        private AudioClip[] Clips()
        {
            var clips = new AudioClip[3];
            for (int n = 0; n < clips.Length; n++)
            {
                clips[n] = AudioClip.Create("Short actual test clip " + n, 17640, 1, 22050, false);
                var samples = new float[17640];
                for (int i = 0; i < samples.Length; i++) samples[i] = .01f * Mathf.Sin(i * (180 + n * 70) * 2 * Mathf.PI / 22050);
                clips[n].SetData(samples, 0); created.Add(clips[n]);
            }
            return clips;
        }
        private BackgroundMusic Music()
        {
            var music = owner.AddComponent<BackgroundMusic>();
            music.SetPlaylist(Clips(), .25f);
            return music;
        }
        private AudioSource[] Voices() { return owner.GetComponentsInChildren<AudioSource>(true); }
        private float PowerLevel() { return Mathf.Sqrt(Voices().Sum(v => v.volume * v.volume)); }

        [Test] public void ThreeOriginalResourcesExistAndUseStreaming()
        {
            Assert.AreEqual(3, BackgroundMusic.ResourcePaths.Length);
            foreach (string path in BackgroundMusic.ResourcePaths)
            {
                AudioClip clip = Resources.Load<AudioClip>(path);
                Assert.NotNull(clip, path);
                Assert.Greater(clip.length, 1, path + " must contain actual music");
                Assert.AreEqual(AudioClipLoadType.Streaming, clip.loadType, path + " must stream instead of keeping decoded songs in memory");
            }
        }

        [UnityTest] public IEnumerator PackagedStreamingMusicActuallyStartsOnAudioSource()
        {
            BackgroundMusic music = owner.AddComponent<BackgroundMusic>();
            float deadline = Time.realtimeSinceStartup + 8;
            while (!Voices().Any(v => v.isPlaying && v.time > .1f) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(music.IsPlaybackActive, "Loaded resource must actually play, not merely select an index");
            Assert.IsTrue(Voices().Any(v => v.isPlaying && v.time > .1f && v.clip != null && v.clip.loadState == AudioDataLoadState.Loaded),
                "A scheduled source is insufficient: the decoded clip cursor must actually advance");
            Assert.AreEqual(3, music.TrackCount);
        }

        [UnityTest] public IEnumerator RealSourcesCrossfadeInOrderIndependentOfTimeScale()
        {
            BackgroundMusic music = Music();
            Time.timeScale = 0;
            var indices = new List<int> { music.CurrentTrackIndex };
            bool overlapping = false;
            float deadline = Time.realtimeSinceStartup + 5;
            while (music.CompletedTransitions < 3 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                AudioSource[] voices = Voices();
                Assert.AreEqual(2, voices.Length);
                foreach (AudioSource voice in voices) Assert.AreEqual(1, voice.pitch);
                if (voices[0].volume > .001f && voices[1].volume > .001f && voices[0].isPlaying && voices[1].isPlaying) overlapping = true;
                if (music.CurrentTrackIndex != indices[indices.Count - 1]) indices.Add(music.CurrentTrackIndex);
            }
            Assert.GreaterOrEqual(music.CompletedTransitions, 3, "DSP playlist stalled with paused scaled time");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0 }, indices.Take(4).ToArray());
            Assert.IsTrue(overlapping, "Both actual sources must overlap with nonzero gains during the fade");
            Assert.LessOrEqual(PowerLevel(), .1001f);
        }

        [UnityTest] public IEnumerator PauseDucksSmoothlyMuteIsImmediateAndQueueContinues()
        {
            BackgroundMusic music = Music();
            yield return new WaitForSecondsRealtime(1.4f);
            float normal = PowerLevel();
            Assert.Greater(normal, .08f);
            music.Paused = true;
            yield return null;
            Assert.Greater(PowerLevel(), normal * .65f, "Pause must duck progressively, not jump to silence");
            yield return new WaitForSecondsRealtime(1);
            Assert.That(PowerLevel(), Is.InRange(.033f, .037f));
            int before = music.CompletedTransitions;
            music.Muted = true;
            Assert.IsTrue(Voices().All(v => v.mute));
            yield return new WaitForSecondsRealtime(.8f);
            Assert.Greater(music.CompletedTransitions, before, "Mute must not rewind or pause the playlist");
            music.Muted = false; music.Paused = false;
            Assert.IsTrue(Voices().All(v => !v.mute && v.pitch == 1));
        }

        [UnityTest] public IEnumerator DisableReenableAndDestroyDoNotLeakOrDestroySuppliedClips()
        {
            BackgroundMusic music = Music();
            yield return new WaitForSecondsRealtime(.4f);
            AudioSource[] voices = Voices();
            music.enabled = false;
            Assert.IsTrue(voices.All(v => !v.isPlaying && v.clip == null));
            music.enabled = true;
            yield return new WaitForSecondsRealtime(.4f);
            Assert.AreEqual(2, Voices().Length);
            Assert.IsTrue(Voices().Any(v => v.isPlaying));
            UnityEngine.Object.Destroy(music);
            yield return null; yield return null;
            Assert.AreEqual(0, Voices().Length);
            Assert.IsTrue(created.All(c => c != null), "Caller owns the injected clips");
        }

        [UnityTest] public IEnumerator ExistingMuteControlsMusicAndKeepsSfxWithoutProceduralBed()
        {
            var effects = owner.AddComponent<ProceduralAudio>();
            var music = owner.GetComponent<BackgroundMusic>();
            Assert.NotNull(music);
            Assert.AreEqual(3, music.TrackCount);
            Assert.AreEqual(6, Voices().Length, "Four event voices plus two music voices; no wind/glass bed");
            effects.Muted = false; effects.ToggleMuted();
            Assert.IsTrue(music.Muted && Voices().All(v => v.mute));
            effects.ToggleMuted();
            Assert.IsFalse(music.Muted);
            effects.enabled = false;
            Assert.IsFalse(music.enabled);
            effects.enabled = true;
            yield return null;
            Assert.IsTrue(music.enabled);
            Assert.AreEqual(6, Voices().Length);
        }
    }
}
