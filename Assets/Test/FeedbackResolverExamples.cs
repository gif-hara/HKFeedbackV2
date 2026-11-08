namespace HKFeedbackV2.Test
{
    public partial class FeedbackResolverExamples
    {
        public int BaseValue { get; set; } = 10;

        [FeedbackResolver]
        public static int Add(int left = 1, int right = 2)
        {
            return left + right;
        }

        [FeedbackResolver]
        public static int Multiply(int value = 2, int multiplier = 3)
        {
            return value * multiplier;
        }

        [FeedbackResolver]
        public static float Multiply(float value = 2, float multiplier = 3)
        {
            return value * multiplier;
        }

        [FeedbackResolver]
        private int AddOffset(int offset = 1)
        {
            return BaseValue + offset;
        }
    }
}
