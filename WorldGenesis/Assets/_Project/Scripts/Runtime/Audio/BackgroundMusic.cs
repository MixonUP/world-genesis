using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldGenesis.Runtime
{
    /// <summary>Presentation-only cyclic playlist. DSP timing and pitch never use simulation speed.</summary>
    [DisallowMultipleComponent]
    public sealed class BackgroundMusic : MonoBehaviour
    {
        public static readonly string[] ResourcePaths = { "Music/WG_MUS_01", "Music/WG_MUS_02", "Music/WG_MUS_03" };
        [SerializeField, Range(0, .3f)] private float masterVolume = .1f;
        [SerializeField, Range(4, 6)] private float crossfadeSeconds = 5;
        private readonly AudioSource[] voices = new AudioSource[2];
        private AudioClip[] playlist = Array.Empty<AudioClip>();
        private GameBootstrap game;
        private int currentVoice, currentIndex, nextIndex, resumeIndex;
        private double currentStart, nextStart, currentEnd;
        private float overlap, gain;
        private bool running, muted, paused;
        public bool HasTracks => playlist.Length > 0;
        public int TrackCount => playlist.Length;
        public bool IsPlaybackActive => running && ((voices[0] != null && voices[0].isPlaying) || (voices[1] != null && voices[1].isPlaying));
        public int CurrentTrackIndex => running ? currentIndex : -1;
        public int CompletedTransitions { get; private set; }
        public float MasterVolume { get => masterVolume; set => masterVolume = Mathf.Clamp(value, 0, .3f); }
        public bool Paused { get => paused; set => paused = value; }
        public bool Muted
        {
            get => muted;
            set { muted = value; for (int i = 0; i < 2; i++) if (voices[i] != null) voices[i].mute = value; }
        }

        private void Awake()
        {
            game = GetComponent<GameBootstrap>();
            for (int i = 0; i < 2; i++)
            {
                var child = new GameObject("Background music " + (i == 0 ? "A" : "B"));
                child.transform.SetParent(transform, false);
                AudioSource source = child.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                source.dopplerLevel = 0; source.pitch = 1; source.volume = 0;
                source.priority = 200; source.ignoreListenerPause = true; voices[i] = source;
            }
            var tracks = new List<AudioClip>();
            foreach (string path in ResourcePaths)
            {
                AudioClip clip = Resources.Load<AudioClip>(path);
                if (clip != null && clip.length > 0) tracks.Add(clip);
            }
            playlist = tracks.ToArray();
            EnsurePlaylistData();
        }

        /// <summary>Inject or replace real clips; this component never destroys caller-owned clips.</summary>
        public void SetPlaylist(AudioClip[] clips, float transitionSeconds = 5)
        {
            StopPlayback();
            var tracks = new List<AudioClip>();
            if (clips != null) foreach (AudioClip clip in clips)
                if (clip != null && clip.length > 0 && !tracks.Contains(clip)) tracks.Add(clip);
            playlist = tracks.ToArray();
            crossfadeSeconds = Mathf.Clamp(transitionSeconds, .05f, 6);
            resumeIndex = 0; CompletedTransitions = 0;
            if (isActiveAndEnabled) BeginPlayback();
        }

        private bool EnsurePlaylistData()
        {
            bool ready = HasTracks;
            foreach (AudioClip clip in playlist)
            {
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                if (clip.loadState != AudioDataLoadState.Loaded) ready = false;
            }
            return ready;
        }
        private void OnEnable() { BeginPlayback(); }
        private void BeginPlayback()
        {
            if (running || !HasTracks || voices[0] == null || !EnsurePlaylistData()) return;
            currentVoice = 0; currentIndex = resumeIndex % playlist.Length;
            currentStart = AudioSettings.dspTime + .25;
            gain = 0; running = true;
            Schedule(voices[currentVoice], playlist[currentIndex], currentStart);
            ScheduleNext();
        }
        private void Schedule(AudioSource voice, AudioClip clip, double start)
        {
            voice.Stop(); voice.clip = clip; voice.pitch = 1; voice.volume = 0;
            voice.mute = muted; voice.PlayScheduled(start);
        }
        private void ScheduleNext()
        {
            nextIndex = (currentIndex + 1) % playlist.Length;
            // Short injected clips retain separate starts; production tracks use the full five seconds.
            overlap = Mathf.Min(crossfadeSeconds, Mathf.Min(playlist[currentIndex].length, playlist[nextIndex].length) * .45f);
            currentEnd = currentStart + playlist[currentIndex].length;
            nextStart = currentEnd - overlap;
            voices[currentVoice].SetScheduledEndTime(currentEnd);
            Schedule(voices[1 - currentVoice], playlist[nextIndex], nextStart);
        }
        private void Update()
        {
            if (game != null) paused = game.Paused;
            if (!running) { BeginPlayback(); if (!running) return; }
            double now = AudioSettings.dspTime;
            if (now >= currentEnd)
            {
                voices[currentVoice].Stop();
                currentVoice = 1 - currentVoice; currentIndex = nextIndex;
                currentStart = nextStart; CompletedTransitions++;
                if (now >= currentStart + playlist[currentIndex].length)
                {
                    // A suspended application may miss a whole clip. Resume the queue without a catch-up burst.
                    currentIndex = (currentIndex + 1) % playlist.Length;
                    currentStart = now + .25;
                    Schedule(voices[currentVoice], playlist[currentIndex], currentStart);
                }
                ScheduleNext();
            }
            gain = Mathf.MoveTowards(gain, paused ? .35f : 1, Time.unscaledDeltaTime * .8f);
            float progress = Mathf.Clamp01((float)((now - nextStart) / Math.Max(.001, overlap)));
            float level = masterVolume * gain;
            voices[currentVoice].volume = level * Mathf.Cos(progress * Mathf.PI * .5f);
            voices[1 - currentVoice].volume = level * Mathf.Sin(progress * Mathf.PI * .5f);
        }
        private void StopPlayback()
        {
            if (running && HasTracks) resumeIndex = (currentIndex + 1) % playlist.Length;
            running = false; gain = 0;
            for (int i = 0; i < 2; i++) if (voices[i] != null) { voices[i].Stop(); voices[i].volume = 0; voices[i].clip = null; }
        }
        private void OnDisable() { StopPlayback(); }
        private void OnDestroy()
        {
            StopPlayback();
            for (int i = 0; i < 2; i++) if (voices[i] != null) { Destroy(voices[i].gameObject); voices[i] = null; }
            playlist = Array.Empty<AudioClip>();
        }
    }
}
