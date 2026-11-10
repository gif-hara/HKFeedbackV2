namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Rigidbody2D
        {
            [FeedbackAction]
            public static void AddForce(
                UnityEngine.Rigidbody2D rigidbody,
                UnityEngine.Vector2 force,
                UnityEngine.ForceMode2D mode = UnityEngine.ForceMode2D.Force)
            {
                rigidbody.AddForce(force, mode);
            }

            [FeedbackAction]
            public static void AddTorque(
                UnityEngine.Rigidbody2D rigidbody,
                float torque,
                UnityEngine.ForceMode2D mode = UnityEngine.ForceMode2D.Force)
            {
                rigidbody.AddTorque(torque, mode);
            }

            [FeedbackAction]
            public static void SetLinearVelocity(UnityEngine.Rigidbody2D rigidbody, UnityEngine.Vector2 linearVelocity)
            {
                rigidbody.linearVelocity = linearVelocity;
            }

            [FeedbackAction]
            public static void SetAngularVelocity(UnityEngine.Rigidbody2D rigidbody, float angularVelocity)
            {
                rigidbody.angularVelocity = angularVelocity;
            }

            [FeedbackAction]
            public static void MovePosition(UnityEngine.Rigidbody2D rigidbody, UnityEngine.Vector2 position)
            {
                rigidbody.MovePosition(position);
            }

            [FeedbackAction]
            public static void MoveRotation(UnityEngine.Rigidbody2D rigidbody, float angle)
            {
                rigidbody.MoveRotation(angle);
            }

            [FeedbackAction]
            public static void SetBodyType(UnityEngine.Rigidbody2D rigidbody, UnityEngine.RigidbodyType2D bodyType)
            {
                rigidbody.bodyType = bodyType;
            }

            [FeedbackAction]
            public static void SetGravityScale(UnityEngine.Rigidbody2D rigidbody, float gravityScale)
            {
                rigidbody.gravityScale = gravityScale;
            }
        }
    }
}
