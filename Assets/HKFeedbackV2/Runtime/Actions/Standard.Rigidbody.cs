namespace HKFeedbackV2
{
    public static partial class Standard
    {
        public static partial class Rigidbody
        {
            [FeedbackAction]
            public static void AddForce(
                UnityEngine.Rigidbody rigidbody,
                UnityEngine.Vector3 force,
                UnityEngine.ForceMode mode = UnityEngine.ForceMode.Force)
            {
                rigidbody.AddForce(force, mode);
            }

            [FeedbackAction]
            public static void AddTorque(
                UnityEngine.Rigidbody rigidbody,
                UnityEngine.Vector3 torque,
                UnityEngine.ForceMode mode = UnityEngine.ForceMode.Force)
            {
                rigidbody.AddTorque(torque, mode);
            }

            [FeedbackAction]
            public static void SetLinearVelocity(UnityEngine.Rigidbody rigidbody, UnityEngine.Vector3 linearVelocity)
            {
                rigidbody.linearVelocity = linearVelocity;
            }

            [FeedbackAction]
            public static void SetAngularVelocity(UnityEngine.Rigidbody rigidbody, UnityEngine.Vector3 angularVelocity)
            {
                rigidbody.angularVelocity = angularVelocity;
            }

            [FeedbackAction]
            public static void MovePosition(UnityEngine.Rigidbody rigidbody, UnityEngine.Vector3 position)
            {
                rigidbody.MovePosition(position);
            }

            [FeedbackAction]
            public static void MoveRotation(UnityEngine.Rigidbody rigidbody, UnityEngine.Vector3 eulerAngles)
            {
                rigidbody.MoveRotation(UnityEngine.Quaternion.Euler(eulerAngles));
            }

            [FeedbackAction]
            public static void SetIsKinematic(UnityEngine.Rigidbody rigidbody, bool isKinematic)
            {
                rigidbody.isKinematic = isKinematic;
            }

            [FeedbackAction]
            public static void SetUseGravity(UnityEngine.Rigidbody rigidbody, bool useGravity)
            {
                rigidbody.useGravity = useGravity;
            }
        }
    }
}
