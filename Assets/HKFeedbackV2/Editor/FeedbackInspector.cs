using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HKFeedbackV2.Editor
{
    [CustomEditor(typeof(MonoBehaviour), true, isFallback = true)]
    [CanEditMultipleObjects]
    internal class FeedbackInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var actionFields = GetActionFields();
            if (actionFields.Count == 0) return null;

            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            foreach (var field in root.Children().OfType<PropertyField>().ToArray())
            {
                if (!actionFields.Contains(field.bindingPath)) continue;

                var propertyPath = field.bindingPath;
                var button = new Button(() => FeedbackGraphSession.Open(target, propertyPath))
                {
                    text = "Edit",
                    tooltip = "Edit this feedback in Graph Toolkit"
                };
                button.SetEnabled(!serializedObject.isEditingMultipleObjects);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.FlexStart;
                field.style.flexGrow = 1;
                field.style.minWidth = 0;
                var index = root.IndexOf(field);
                field.RemoveFromHierarchy();
                row.Add(field);
                row.Add(button);
                root.Insert(index, row);
            }
            return root;
        }

        private HashSet<string> GetActionFields()
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var fieldType = field.FieldType;
                    if ((fieldType == typeof(IAction) || fieldType == typeof(IAction[])) &&
                        field.IsDefined(typeof(SerializeReference), false))
                    {
                        fields.Add(field.Name);
                    }
                }
            }
            return fields;
        }
    }

    [CustomEditor(typeof(ScriptableObject), true, isFallback = true)]
    [CanEditMultipleObjects]
    internal sealed class FeedbackAssetInspector : FeedbackInspector { }
}
