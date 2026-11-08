using System;
using UnityEngine;

namespace HKFeedbackV2
{
    public static partial class Standard
    {
        [FeedbackEvaluator]
        public static bool All(IContainer container, IEvaluator[] evaluators)
        {
            foreach (var evaluator in evaluators)
            {
                if (!evaluator.Evaluate(container))
                {
                    return false;
                }
            }

            return true;
        }

        [FeedbackEvaluator]
        public static bool Any(IContainer container, IEvaluator[] evaluators)
        {
            foreach (var evaluator in evaluators)
            {
                if (evaluator.Evaluate(container))
                {
                    return true;
                }
            }

            return false;
        }

        [FeedbackEvaluator]
        public static bool Not(IContainer container, IEvaluator evaluator)
        {
            return !evaluator.Evaluate(container);
        }

        [FeedbackEvaluator]
        public static bool Equal(bool left, bool right)
        {
            return left == right;
        }

        [FeedbackEvaluator]
        public static bool Equal(int left, int right)
        {
            return left == right;
        }

        [FeedbackEvaluator]
        public static bool Equal(float left, float right)
        {
            return left == right;
        }

        [FeedbackEvaluator]
        public static bool Equal(string left, string right)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        [FeedbackEvaluator]
        public static bool NotEqual(bool left, bool right)
        {
            return left != right;
        }

        [FeedbackEvaluator]
        public static bool NotEqual(int left, int right)
        {
            return left != right;
        }

        [FeedbackEvaluator]
        public static bool NotEqual(float left, float right)
        {
            return left != right;
        }

        [FeedbackEvaluator]
        public static bool NotEqual(string left, string right)
        {
            return !string.Equals(left, right, StringComparison.Ordinal);
        }

        [FeedbackEvaluator]
        public static bool GreaterThan(int left, int right)
        {
            return left > right;
        }

        [FeedbackEvaluator]
        public static bool GreaterThan(float left, float right)
        {
            return left > right;
        }

        [FeedbackEvaluator]
        public static bool GreaterThanOrEqual(int left, int right)
        {
            return left >= right;
        }

        [FeedbackEvaluator]
        public static bool GreaterThanOrEqual(float left, float right)
        {
            return left >= right;
        }

        [FeedbackEvaluator]
        public static bool LessThan(int left, int right)
        {
            return left < right;
        }

        [FeedbackEvaluator]
        public static bool LessThan(float left, float right)
        {
            return left < right;
        }

        [FeedbackEvaluator]
        public static bool LessThanOrEqual(int left, int right)
        {
            return left <= right;
        }

        [FeedbackEvaluator]
        public static bool LessThanOrEqual(float left, float right)
        {
            return left <= right;
        }

        [FeedbackEvaluator]
        public static bool Approximately(float left, float right)
        {
            return Mathf.Approximately(left, right);
        }

        [FeedbackEvaluator]
        public static bool IsInRange(int value, int minInclusive, int maxInclusive)
        {
            return value >= minInclusive && value <= maxInclusive;
        }

        [FeedbackEvaluator]
        public static bool IsInRange(float value, float minInclusive, float maxInclusive)
        {
            return value >= minInclusive && value <= maxInclusive;
        }

        [FeedbackEvaluator]
        public static bool IsNull(object value)
        {
            return value == null;
        }

        [FeedbackEvaluator]
        public static bool IsNull(UnityEngine.Object value)
        {
            return value == null;
        }

        [FeedbackEvaluator]
        public static bool IsNotNull(object value)
        {
            return value != null;
        }

        [FeedbackEvaluator]
        public static bool IsNotNull(UnityEngine.Object value)
        {
            return value != null;
        }

        [FeedbackEvaluator]
        public static bool IsNullOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value);
        }

        [FeedbackEvaluator]
        public static bool IsNullOrWhiteSpace(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }
    }
}
