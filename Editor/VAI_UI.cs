using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Thry.VRChatAssetInstaller
{
    public class VAI_UI : EditorWindow
    {
        [MenuItem("Thry/VRC Asset Installer")]
        public static void ShowWindow()
        {
            VAI_UI window = GetWindow<VAI_UI>("VAI");
            window.titleContent = new GUIContent("VRChat Asset Installer");
            window.Show();
        }

        static GUIStyle s_largeTextField => new GUIStyle(EditorStyles.textField) { fontSize = 25 };
        static GUIStyle s_largeTextFieldCentered => new GUIStyle(EditorStyles.textField) { fontSize = 25, alignment = TextAnchor.MiddleCenter };
        static GUIStyle s_largePlaceholderTextField => new GUIStyle(EditorStyles.textField) { fontSize = 25, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, normal = new GUIStyleState() { textColor = Color.gray } };
        static GUIStyle s_richLabelCentered => new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleCenter };
        static GUIStyle s_richLabelWithLineBreaks => new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
        static Color COLOR_BACKGROUND => EditorGUIUtility.isProSkin ? new Color(0.27f, 0.27f, 0.27f) : new Color(0.65f, 0.65f, 0.65f);

        string _searchTerm = "";
        Vector2 _scrollPos;
        bool _isAnyAssetBeingModified = false;

        private void OnGUI()
        {
            if(!VAI.StartedLoading) VAI.Reload();

            EditorGUI.BeginDisabledGroup(VAI.IsLoading);
            if (GUILayout.Button(VAI.IsLoading ? "Loading..." : "Reload"))
            {
                VAI.Reload();
            }
            EditorGUI.EndDisabledGroup();

            _searchTerm = EditorGUILayout.TextField(_searchTerm, s_largeTextFieldCentered, GUILayout.Height(40));
            if(_searchTerm.Length == 0)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                EditorGUI.LabelField(r, "Search...", s_largePlaceholderTextField);
            }

            if(VAI.IsLoading && VAI.CuratedAssets.Length == 0)
            {
                GUILayout.Label($"<size=25>Loading...</size>", s_richLabelCentered, GUILayout.ExpandWidth(true), GUILayout.Height(100));
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            _isAnyAssetBeingModified = VAI.CuratedAssets.Any(a => a.IsBeingModified) || VAI.OtherAssets.Any(a => a.IsBeingModified);

            AssetCollectionUI(VAI.CuratedAssets, "Curated");
            AssetCollectionUI(VAI.OtherAssets, "Others");

            EditorGUILayout.EndScrollView();
        }

        void AssetCollectionUI(VAI.AssetInfo[] assets, string headerName)
        {
            VAI.AssetInfo[] filtered = assets.Where(a =>
                string.IsNullOrEmpty(_searchTerm) || a.name.ToLower().Contains(_searchTerm.ToLower())).ToArray();

            if(filtered.Length == 0)
            {
                return;
            }
            GUILayout.Label($"<size=25>{headerName}</size>", s_richLabelCentered, GUILayout.ExpandWidth(true), GUILayout.Height(40));
            for(int i = 0; i < filtered.Length; i++)
            {
                AssetUI(filtered[i]);
                if(i < filtered.Length - 1)
                {
                    EditorGUI.DrawRect(EditorGUILayout.GetControlRect(GUILayout.Height(2)), Color.gray);
                }
            }
        }

        void AssetUI(VAI.AssetInfo asset)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_isAnyAssetBeingModified);
            if(asset.IsBeingModified)
            {
                GUILayout.Button(asset.IsBeingInstalled ? "Adding..." : "Removing...", GUILayout.Width(100));
            }
            else if(asset.IsInstalled)
            {
                if(GUILayout.Button("Remove", GUILayout.Width(100)))
                {
                    VAI.RemoveAsset(asset);
                }
            }
            else
            {
                if (GUILayout.Button("Add", GUILayout.Width(100)))
                {
                    VAI.InstallAsset(asset);
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.Label(asset.name, GUILayout.Width(200));
            GUILayout.Label(asset.author, GUILayout.Width(100));
            GUILayout.FlexibleSpace();
            if(asset.IsInstalled && asset.HasUpdate)
            {
                if(GUILayout.Button("Update", GUILayout.Width(80)))
                {
                    VAI.InstallAsset(asset);
                }
            }
            if(GUILayout.Button(asset.IsUIExpaned ? "▲ Details ▲" : "▼ Details ▼", GUILayout.Width(100)))
            {
                asset.IsUIExpaned = !asset.IsUIExpaned;
            }
            EditorGUILayout.EndHorizontal();

            if(asset.IsUIExpaned)
            {
                Rect r_bg = EditorGUILayout.BeginHorizontal();
                r_bg = new RectOffset(5,5,5,5).Remove(r_bg);
                GUI.DrawTexture(r_bg, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, COLOR_BACKGROUND, 0, 10);

                EditorGUILayout.Space(10, false);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.Space(5);

                GUILayout.Label(asset.description, s_richLabelWithLineBreaks);
                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("Type: ", asset.type.ToString());
                if(asset.Type == VAI.AssetType.UPM)
                {
                    EditorGUILayout.LabelField("UPM Id: ", asset.packageId);
                }
                EditorGUILayout.LabelField("Url: ", asset.git);
                if(Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    Event.current.Use();
                    Application.OpenURL(asset.git);
                }
                if(asset.IsInstalled)
                {
                    string path = AssetDatabase.GUIDToAssetPath(asset.guid);
                    if(string.IsNullOrWhiteSpace(path) == false && GUILayout.Button("Locate"))
                    {
                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath(path, typeof(Object)));
                    }
                    if(asset.Type == VAI.AssetType.UNITYPACKAGE)
                    {
                        if(GUILayout.Button("Remove & Reinstall newest version"))
                        {
                            VAI.RemoveAsset(asset);
                            VAI.InstallAsset(asset);
                        }
                    }
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10, false);
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}