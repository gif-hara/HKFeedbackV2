namespace HKFeedbackV2.Test
{
    public partial class FeedbackEvaluatorExamples
    {
        public int Threshold { get; set; } = 10;

        [FeedbackEvaluator]
        public static bool IsPositive(float value = 1)
        {
            return value > 0;
        }

        [FeedbackEvaluator]
        public static bool Equal(int value = 1, int expected = 1)
        {
            return value == expected;
        }

        [FeedbackEvaluator]
        public static bool Equal(float value = 1, float expected = 1)
        {
            return value == expected;
        }

        [FeedbackEvaluator]
        private bool ExceedsThreshold(int value = 11)
        {
            return value > Threshold;
        }
    }
}
