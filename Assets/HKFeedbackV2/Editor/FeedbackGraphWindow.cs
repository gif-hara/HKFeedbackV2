using System;
using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;

namespace HKFeedbackV2.Editor
{
    [NoAutoStaticsCleanup]
    internal static class FeedbackGraphWindow
    {
        // GraphDatabase attaches new graphs to asset files. The window can open the same model in memory.
        private const string Implementation = "Unity.GraphToolkit.Editor.Implementation.";
        private static readonly Assembly Toolkit = typeof(Graph).Assembly;
        private static readonly Type GraphObjectType = Toolkit.GetType(Implementation + "GraphObjectImp", throwOnError: true);
        private static readonly Type GraphModelType = Toolkit.GetType(Implementation + "GraphModelImp", throwOnError: true);
        private static readonly Type WindowType = Toolkit.GetType(Implementation + "GraphViewEditorWindowImp", throwOnError: true);
        private static readonly MethodInfo ShowWindow = WindowType.BaseType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "ShowGraphInExistingOrNewWindow" && !method.IsGenericMethod);
        private static readonly PropertyInfo GraphDirty = GraphObjectType.GetProperty("Dirty");
        private static readonly PropertyInfo HasUnsavedChanges = typeof(EditorWindow).GetProperty(nameof(EditorWindow.hasUnsavedChanges));

        internal static ScriptableObject CreateGraph(string name, out FeedbackGraph graph)
        {
            var graphObject = ScriptableObject.CreateInstance(GraphObjectType);
            try
            {
                graphObject.name = name;
                graphObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                GraphObjectType.GetProperty("GraphType").SetValue(graphObject, typeof(FeedbackGraph));
                GraphObjectType.GetMethod("CreateMainGraph").Invoke(graphObject, new object[] { GraphModelType });
                var model = GraphObjectType.GetProperty("GraphModel").GetValue(graphObject);
                graph = (FeedbackGraph)GraphModelType.GetProperty("Graph").GetValue(model);
                return graphObject;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(graphObject);
                throw;
            }
        }

        internal static EditorWindow Show(ScriptableObject graphObject) =>
            (EditorWindow)ShowWindow.Invoke(null, new object[] { graphObject, WindowType, null });

        internal static void ClearDirty(ScriptableObject graphObject) => GraphDirty.SetValue(graphObject, false);

        internal static void Close(EditorWindow window)
        {
            HasUnsavedChanges.SetValue(window, false);
            window.Close();
        }
    }
}
