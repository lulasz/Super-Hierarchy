#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lulasz.Hierarchy
{
    internal enum StickyIcon
    {
        Never,
        OnAnyObject,
        NotOnPrefabs
    }
    internal enum TransformIcon
    {
        Never,
        Always,
        OnUniqueOrigin,
        OnlyRectTransform
    }

    internal class HierarchyPreferences : ScriptableObject
    {
        public bool enableSmartHierarchy = true;
        public StickyIcon stickyComponentIcon = StickyIcon.NotOnPrefabs;
        public TransformIcon transformIcon = TransformIcon.OnUniqueOrigin;
    }
}
#endif
