﻿#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static System.Linq.Expressions.Expression;
using Object = UnityEngine.Object;

namespace Lulasz.Hierarchy
{
    [InitializeOnLoad]
    internal static class SuperHierarchy
    {
        private static Texture2D folderIcon;
        private static Texture2D folderEmptyIcon;
        private static GUIStyle iconStyle;

        private class ItemData
        {
            internal GameObject instance;
            internal int id;

            internal TreeViewItem view;
            internal Texture2D icon;
            internal int initialDepth;
            internal int lastViewId;
            internal bool wasExpanded;

            internal bool isPrefab;
            internal bool isRootPrefab;
            internal bool isFolder;
        }
        private class FolderData
        {
        }

        private static readonly Dictionary<int, ItemData> ItemsData = new Dictionary<int, ItemData>();
        private static readonly Dictionary<int, FolderData> FoldersData = new Dictionary<int, FolderData>();

        static SuperHierarchy()
        {
            Selection.selectionChanged += ReloadView;
            Reflected.onVisibleRowsChanged += ReloadView;
            EditorApplication.hierarchyChanged += ReloadView;

            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;

            folderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
            folderEmptyIcon = EditorGUIUtility.IconContent("FolderEmpty Icon").image as Texture2D;
        }

        private static void ReloadView()
        {
            ItemsData.Clear();
            FoldersData.Clear();
        }

        private static void OnHierarchyItemGUI(int instanceId, Rect rect)
        {
            if (iconStyle == null)
            {
                iconStyle = new GUIStyle(EditorStyles.label)
                {
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }


            if (!ItemsData.TryGetValue(instanceId, out var item))
            {
                var instance = EditorUtility.InstanceIDToObject(instanceId) as GameObject;

                if (instance == null)
                    return;

                item = new ItemData
                {
                    instance = instance,
                    isPrefab = PrefabUtility.GetPrefabAssetType(instance) == PrefabAssetType.Regular,
                    isRootPrefab = PrefabUtility.IsAnyPrefabInstanceRoot(instance),
                    isFolder = instance.TryGetComponent<Folder>(out _)
                };
                var components = instance.GetComponents<Component>();

                var mainComponent = DecideMainComponent(components);
                if (mainComponent != null)
                    item.icon = EditorGUIUtility.ObjectContent(mainComponent, mainComponent.GetType()).image as Texture2D;

                ItemsData.Add(instanceId, item);
            }

            if (item.instance == null)
            {
                ItemsData.Remove(instanceId);
                return;
            }

            var fullWidthRect = GetFullWidthRect(rect);

            // Happens to be null when entering prefab mode
            if (item.view == null)
            {
                item.view = Reflected.GetViewItem(instanceId);
                if (item.view != null)
                {
                    item.initialDepth = item.view.depth;
                }
                else
                {
                    return;
                }
            }

            HandleItemView(item);

            if (IsHoveringItem(fullWidthRect))
            {
                var toggleRect = new Rect(fullWidthRect) { x = 32 };
                if (OnLeftToggle(toggleRect, item.instance.activeSelf, out var isActive))
                {
                    Undo.RecordObject(item.instance, "GameObject Set Active");
                    item.instance.SetActive(isActive);
                }
            }
        }

        private static void HandleItemView(ItemData item)
        {
            var id = item.id;
            var instance = item.instance;

            if (item.isFolder)
            {
                if (!FoldersData.TryGetValue(id, out var folder))
                {
                    FoldersData.Add(id, folder);
                }

                item.view.icon = instance.transform.childCount == 0 ? folderEmptyIcon : folderIcon;
            }
            else
            {
                if (item.icon != null)
                {
                    //switch (preferences.stickyComponentIcon)
                    //{
                    //    case StickyIcon.Never: break;
                    //    case StickyIcon.OnAnyObject:
                    //        item.view.icon = item.icon;
                    //        break;
                    //    case StickyIcon.NotOnPrefabs:
                            if (!item.isRootPrefab)
                                item.view.icon = item.icon;
                    //        break;
                    //}
                }

                if (Application.isPlaying)
                    item.view.depth = item.initialDepth;
            }
        }

        internal static Component DecideMainComponent(params Component[] components)
        {
            var count = components.Length;
            if (count == 0)
                return null;

            var zeroComponent = components[0];

            if (count == 1)
            {
                // Only on RectTransform
                return zeroComponent is RectTransform ? zeroComponent : null;
            }

            if (HasCanvasRenderer(components))
            {
                return GetMainUGUIComponent(components);
            }

            return components[1];
        }

        private static bool HasCanvasRenderer(params Component[] components)
        {
            return components.OfType<CanvasRenderer>().Any();
        }

        private static Component GetMainUGUIComponent(params Component[] components)
        {
            Component lastComponent = null;
            UIBehaviour firstUIBehaviour = null;

            foreach (var component in components)
            {
                if (component is Graphic graphic)
                    lastComponent = graphic;

                if (!firstUIBehaviour && component is UIBehaviour uiBehaviour)
                {
                    firstUIBehaviour = uiBehaviour;
                    lastComponent = uiBehaviour;
                }

                if (component is Selectable selectable)
                    lastComponent = selectable;
            }

            return lastComponent;
        }

        private static Rect GetFullWidthRect(Rect rect)
        {
            var fullWidthRect = new Rect(rect);
            fullWidthRect.x = 0;
            fullWidthRect.width = Screen.width;
            return fullWidthRect;
        }

        private static bool IsHoveringItem(Rect rect)
        {
            return rect.Contains(Event.current.mousePosition);
        }

        private static bool OnLeftToggle(Rect rect, bool isActive, out bool value)
        {
            var toggleRect = new Rect(rect) { width = 16 };

            EditorGUI.BeginChangeCheck();
            value = GUI.Toggle(toggleRect, isActive, GUIContent.none);
            return EditorGUI.EndChangeCheck();
        }

        internal class TreeViewController
        {
            public object controller; // TreeViewController
            public object data; // GameObjectTreeViewDataSource

            // Takes data, row index and returns item id
            public static Func<object, int, int> GetRow;
            // Takes data and item id
            public static Func<object, int, TreeViewItem> GetItem;
            public static Func<object, int, bool> IsExpanded;

            private static PropertyInfo getDataProperty;
            private static PropertyInfo getStateProperty;
            private static FieldInfo onVisibleRowsChangedField;


            [InitializeOnLoadMethod]
            private static void OnInitialize()
            {
                var treeViewControllerType =
                    typeof(TreeViewState).Assembly.GetType("UnityEditor.IMGUI.Controls.TreeViewController");

                getDataProperty = treeViewControllerType.GetProperty("data");
                getStateProperty = treeViewControllerType.GetProperty("state");

                var treeViewDataType = typeof(Editor).Assembly.GetType("UnityEditor.GameObjectTreeViewDataSource");

                onVisibleRowsChangedField =
                    treeViewDataType.GetField("onVisibleRowsChanged");

                var getRowMethod = treeViewDataType.GetMethod("GetRow");
                var getItemMethod = treeViewDataType.GetMethod("GetItem");
                var isExpandedMethod = treeViewDataType.GetMethod("IsExpanded", new[] { typeof(int) });

                var objParam = Parameter(typeof(object));
                var intParam = Parameter(typeof(int));
                var dataTypeConvert = Convert(objParam, treeViewDataType);

                GetRow = Lambda<Func<object, int, int>>(
                        Call(dataTypeConvert, getRowMethod, intParam), objParam, intParam).Compile();

                GetItem = Lambda<Func<object, int, TreeViewItem>>(
                    Call(dataTypeConvert, getItemMethod, intParam), objParam, intParam).Compile();

                IsExpanded = Lambda<Func<object, int, bool>>(
                    Call(dataTypeConvert, isExpandedMethod, intParam), objParam, intParam).Compile();
            }

            public void Assign(object controller)
            {
                this.controller = controller;

                data = getDataProperty.GetValue(controller);
            }

            public void SetOnVisibleRowsChanged(Action action)
            {
                var onVisibleRowsChanged = onVisibleRowsChangedField.GetValue(data) as Action;
                onVisibleRowsChanged += action;
                onVisibleRowsChangedField.SetValue(data, onVisibleRowsChanged);
            }
        }

        internal static class Reflected
        {
            public static TreeViewController CurrentTreeView;
            public static Action onExpandedStateChange;
            public static Action onVisibleRowsChanged;
            public static Action onTreeViewReload;

            // We need to get SceneHierarchy TreeView to change items icon
            private static readonly PropertyInfo getLastInteractedHierarchyWindow;
            private static readonly PropertyInfo getSceneHierarchy;
            private static readonly FieldInfo getTreeViewController;

            private static readonly MethodInfo frameObject;

            private static readonly Func<object> GetLastHierarchyWindow;

            private static Dictionary<object, TreeViewController> HierarchyTreeViewStates = new Dictionary<object, TreeViewController>();

            static Reflected()
            {
                var sceneHierarchyWindowType = typeof(Editor).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
                var sceneHierarchyType = typeof(Editor).Assembly.GetType("UnityEditor.SceneHierarchy");

                // As all required types are internal, we need to do some reflection
                // See https://github.com/Unity-Technologies/UnityCsReference/blob/2020.1/Editor/Mono/SceneHierarchyWindow.cs

                getLastInteractedHierarchyWindow = sceneHierarchyWindowType
                    .GetProperty("lastInteractedHierarchyWindow", BindingFlags.Public | BindingFlags.Static);
                getSceneHierarchy = sceneHierarchyWindowType.GetProperty("sceneHierarchy");
                getTreeViewController = sceneHierarchyType.GetField("m_TreeView", BindingFlags.NonPublic | BindingFlags.Instance);

                frameObject = sceneHierarchyWindowType.GetMethod("FrameObject");

                GetLastHierarchyWindow = Lambda<Func<object>>(Property(null, getLastInteractedHierarchyWindow)).Compile();
            }

            public static void FrameObject(int instanceId)
            {
                var hierarchyWindow = GetLastHierarchyWindow();

                frameObject.Invoke(hierarchyWindow, new object[] { instanceId, false });
            }

            public static TreeViewItem GetViewItem(int id)
            {
                var controller = GetLastTreeViewController();

                // GetRow checks every rows for required id.
                // It's much faster then recursive FindItem, but still needs to be called only when TreeView is changed.
                var row = TreeViewController.GetRow(controller.data, id);
                if (row == -1)
                    return null;

                // There's an error during undo
                try
                {
                    return TreeViewController.GetItem(controller.data, row);
                }
                catch
                {
                    return null;
                }
            }

            public static TreeViewController GetLastTreeViewController()
            {
                var hierarchyWindow = GetLastHierarchyWindow();

                // Reflection performance is not so bad comparing to FindItem.. 
                var sceneHierarchy = getSceneHierarchy.GetValue(hierarchyWindow);
                var treeViewController = getTreeViewController.GetValue(sceneHierarchy);

                if (!HierarchyTreeViewStates.TryGetValue(hierarchyWindow, out var treeViewState))
                {
                    treeViewState = new TreeViewController();

                    HierarchyTreeViewStates.Add(hierarchyWindow, treeViewState);
                }

                // Happens when entering/exiting Prefab Mode
                if (treeViewController != treeViewState.controller)
                {
                    treeViewState.Assign(treeViewController);
                    treeViewState.SetOnVisibleRowsChanged(onVisibleRowsChanged);
                    onTreeViewReload?.Invoke();
                }

                return treeViewState;
            }
        }
    }
}
#endif
