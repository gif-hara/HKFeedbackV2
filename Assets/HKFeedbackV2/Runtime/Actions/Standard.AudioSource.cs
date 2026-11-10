namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class AudioSource
        {
            [FeedbackAction]
            public static void Play(UnityEngine.AudioSource audioSource)
            {
                audioSource.Play();
            }

            [FeedbackAction]
            public static void Pause(UnityEngine.AudioSource audioSource)
            {
                audioSource.Pause();
            }

            [FeedbackAction]
            public static void UnPause(UnityEngine.AudioSource audioSource)
            {
                audioSource.UnPause();
            }

            [FeedbackAction]
            public static void Stop(UnityEngine.AudioSource audioSource)
            {
                audioSource.Stop();
            }

            [FeedbackAction]
            public static void PlayOneShot(UnityEngine.AudioSource audioSource, UnityEngine.AudioClip clip, float volumeScale = 1f)
            {
                audioSource.PlayOneShot(clip, volumeScale);
            }

            [FeedbackAction]
            public static void SetVolume(UnityEngine.AudioSource audioSource, float volume)
            {
                audioSource.volume = volume;
            }

            [FeedbackAction]
            public static void SetPitch(UnityEngine.AudioSource audioSource, float pitch)
            {
                audioSource.pitch = pitch;
            }
        }
    }
}
