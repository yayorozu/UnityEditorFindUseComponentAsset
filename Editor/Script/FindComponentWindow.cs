using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Yorozu.EditorTool
{
	/// <summary>
	/// 対象のコンポーネントを探す
	/// </summary>
	internal class FindComponentWindow : EditorWindow
	{
		[MenuItem("Tools/Find Use Component")]
		private static void ShowWindow()
		{
			var window = GetWindow<FindComponentWindow>();
			window.titleContent = new GUIContent("Find Component");
			window.Show();
		}

		private string _searchString;
		private SearchField _searchField;
		private SearchType[] _searchTypes;
		private string[] _popSearches;
		private int _popupIndex;
		private List<FindAsset> _findAssets;
		private Vector2 _scroll;

		private void OnEnable()
		{
			if (_searchField == null)
			{
				_searchField = new SearchField();
			}

			_searchTypes = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(MonoBehaviour)))
				.OrderBy(t => t.Namespace)
				.Select(t => new SearchType(t))
				.ToArray();

			SetPopSearch();
		}

		private void OnGUI()
		{
			using (var check = new EditorGUI.ChangeCheckScope())
			{
				_searchString = _searchField.OnGUI(_searchString);
				if (check.changed)
				{
					_popupIndex = 0;
					SetPopSearch();
				}
			}

			_popupIndex = EditorGUILayout.Popup("SearchType", _popupIndex, _popSearches);

			if (GUILayout.Button("Find"))
			{
				if (_popSearches != null && _popupIndex < _popSearches.Length)
				{
					var full = _popSearches[_popupIndex];
					var lastIndex = full.LastIndexOf(".", StringComparison.Ordinal);
					var ns = lastIndex >= 0 ? full.Substring(0, lastIndex) : null;
					var name = lastIndex >= 0 ? full.Substring(lastIndex + 1) : full;

					var find = _searchTypes.FirstOrDefault(t => t.Name == name && t.Namespace == ns);
					if (find != null)
					{
						Find(find);
					}
				}
			}

			if (_findAssets == null)
				return;

			using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
			{
				_scroll = scroll.scrollPosition;
				foreach (var asset in _findAssets)
				{
					asset.OnGUI();
				}
			}
		}

		private void SetPopSearch()
		{
			var empty = string.IsNullOrEmpty(_searchString);
			_popSearches = _searchTypes
				.Where(t => empty || t.Name.IndexOf(_searchString, StringComparison.Ordinal) >= 0)
				.Select(t => t.DisplayName)
				.ToArray();
		}

		private void Find(SearchType searchTarget)
		{
			// 検索する Type を探す
			var type = AppDomain.CurrentDomain
				.GetAssemblies()
				.Where(a => a.FullName == searchTarget.AssemblyName)
				.SelectMany(a => a.GetTypes())
				.FirstOrDefault(t => t.Name == searchTarget.Name && t.Namespace == searchTarget.Namespace);

			if (type == null)
			{
				Debug.LogError($"{searchTarget.Name} Not Found.");
				return;
			}

			var targetPath = "";

			var guids = AssetDatabase.FindAssets("t:prefab t:Scene", new[] {"Assets"});

			void Progress(int index)
			{
				if (index % 25 != 0)
					return;

				EditorUtility.DisplayProgressBar(
					"Search Implement Component",
					$"{index} / {guids.Length}",
					index / (float)guids.Length
					);
			}

			bool IsTargetClass(string path)
			{
				if (!path.EndsWith(".cs"))
					return false;

				if (string.IsNullOrEmpty(targetPath))
				{
					var fileName = Path.GetFileNameWithoutExtension(path);
					if (fileName != searchTarget.Name)
						return false;

					var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
					if (script.GetClass() == type)
					{
						targetPath = path;
					}
				}

				return targetPath == path;
			}

			_findAssets = new List<FindAsset>();
			try
			{
				for (var i = 0; i < guids.Length; i++)
				{
					Progress(i);

					var guid = guids[i];
					var path = AssetDatabase.GUIDToAssetPath(guid);
					var dependencies = AssetDatabase.GetDependencies(path);
					foreach (var dependency in dependencies)
					{
						if (!IsTargetClass(dependency))
							continue;

						_findAssets.Add(new FindAsset(path, type));
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			_findAssets.OrderBy(a => a.Path);
		}
	}

	[Serializable]
	internal class SearchType
	{
		internal string AssemblyName;
		internal string Namespace;
		internal string Name;
		
		internal string DisplayName
		{
			get
			{
				if (string.IsNullOrEmpty(Namespace))
					return Name;

				return $"{Namespace}.{Name}";
			}
		}

		internal SearchType(Type type)
		{
			AssemblyName = type.Assembly.FullName;
			Namespace = type.Namespace;
			Name = type.Name;
		}
	}

	[Serializable]
	internal class FindAsset
	{
		internal string Path;
		private Component[] _finds;
		private Object _asset;

		internal FindAsset(string path, Type type)
		{
			_asset = AssetDatabase.LoadAssetAtPath<Object>(path);
			Path = path;
			// Prefab だったら探す
			if (path.EndsWith(".prefab"))
			{
				var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				_finds = prefab.GetComponentsInChildren(type);
			}
			var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
		}

		internal void OnGUI()
		{
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField(_asset, typeof(Object), false);
				if (_finds != null)
				{
					using (new EditorGUI.IndentLevelScope())
					{
						foreach (var component in _finds)
						{
							EditorGUILayout.ObjectField(component, typeof(Object), false);
						}
					}
				}
			}
		}
	}
}
