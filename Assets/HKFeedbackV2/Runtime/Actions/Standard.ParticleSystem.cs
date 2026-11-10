namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class ParticleSystem
        {
            [FeedbackAction]
            public static void Play(UnityEngine.ParticleSystem particleSystem, bool withChildren = true)
            {
                particleSystem.Play(withChildren);
            }

            [FeedbackAction]
            public static void Pause(UnityEngine.ParticleSystem particleSystem, bool withChildren = true)
            {
                particleSystem.Pause(withChildren);
            }

            [FeedbackAction]
            public static void Stop(
                UnityEngine.ParticleSystem particleSystem,
                bool withChildren = true,
                UnityEngine.ParticleSystemStopBehavior stopBehavior = UnityEngine.ParticleSystemStopBehavior.StopEmitting)
            {
                particleSystem.Stop(withChildren, stopBehavior);
            }

            [FeedbackAction]
            public static void Clear(UnityEngine.ParticleSystem particleSystem, bool withChildren = true)
            {
                particleSystem.Clear(withChildren);
            }

            [FeedbackAction]
            public static void Emit(UnityEngine.ParticleSystem particleSystem, int count)
            {
                particleSystem.Emit(count);
            }
        }
    }
}
