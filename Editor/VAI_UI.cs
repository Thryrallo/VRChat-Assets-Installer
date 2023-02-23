using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thry.VRChatAssetInstaller
{
    public class VAI_UI : EditorWindow
    {
        [MenuItem("Thry/VRC Assets Installer")]
        public static void ShowWindow()
        {
            VAI_UI window = GetWindow<VAI_UI>("VAI");
            window.Show();
        }

        static GUIStyle s_largeTextField = new GUIStyle(EditorStyles.textField) { fontSize = 25 };
        static GUIStyle s_richLabelCentered = new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleCenter };

        string _searchTerm = "";
        bool _searchShowPlaceholder = true;

        private void OnGUI()
        {
            if(!VAI.StartedLoading) VAI.Reload();

            EditorGUI.BeginDisabledGroup(VAI.IsLoading);
            if (GUILayout.Button("Reload"))
            {
                VAI.Reload();
            }
            EditorGUI.EndDisabledGroup();

            Rect r_search = EditorGUILayout.GetControlRect(GUILayout.Height(40));
            // check if user clicked the text field
            if( _searchShowPlaceholder && Event.current.type == EventType.MouseDown && r_search.Contains(Event.current.mousePosition) )
            {
                _searchShowPlaceholder = false;
                _searchTerm = "";
                Repaint();
            }
            if(_searchShowPlaceholder)
            {
                EditorGUI.LabelField(r_search, "Search...", s_largeTextField);
            }
            else
            {
                _searchTerm = EditorGUI.TextField(r_search, _searchTerm, s_largeTextField);
            }

            GUILayout.Label("<size=25>Curated</size>", s_richLabelCentered, GUILayout.ExpandWidth(true), GUILayout.Height(40));
            foreach(VAI.AssetInfo asset in VAI.CuratedAssets)
            {
                AssetUI(asset);
            }

            GUILayout.Label("<size=25>Others</size>", s_richLabelCentered, GUILayout.ExpandWidth(true), GUILayout.Height(40));
            foreach (VAI.AssetInfo asset in VAI.OtherAssets)
            {
                AssetUI(asset);
            }
        }

        void AssetUI(VAI.AssetInfo asset)
        {
            if( !string.IsNullOrEmpty(_searchTerm) && !asset.name.ToLower().Contains(_searchTerm.ToLower()) )
            {
                return;
            }


        }
    }
}