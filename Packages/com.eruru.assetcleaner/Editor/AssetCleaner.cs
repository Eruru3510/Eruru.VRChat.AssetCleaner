#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eruru.AssetCleaner {

	public class AssetCleaner : EditorWindow {

		const string Name = "Asset Cleaner";
		static readonly GUIContent NameGUIContent = new (Name);
		static readonly GUIContent AnalysisGUIContent = new ("Analysis");
		static readonly GUIContent CollectingFilesGUIContent = new ("Stage {0}/{1} Collecting files {2}/{3}");
		static readonly GUIContent CheckSceneDependenciesGUIContent = new ("Stage {0}/{1} Checking scene {2}/{3} dependencies {4}/{5}");
		static readonly GUIContent CheckScriptDependenciesGUIContent = new ("Stage {0}/{1} Checking script dependencies {2}/{3}");
		static readonly GUIContent SummaryFilesGUIContent = new ("Stage {0}/{1} Summary files {2}/{3}");
		static readonly GUIContent IsolationUnusedFilesGUIContent = new ("Isolation unused files");
		static readonly GUIContent MovingFilesGUIContent = new ("Moving files");
		static readonly GUIContent RestoreFilesGUIContent = new ("Restore files");
		static readonly GUIContent NoUnusedFileGUIContent = new ("No unused file");
		static readonly GUIContent PleaseSaveSceneGUIContent = new ("Please save all current scene first");
		static readonly GUIContent OkGUIContent = new ("OK");
		static readonly GUIContent OpenIsolationDirectoryGUIContent = new ("Open Isolation Folder");
		static readonly Dictionary<string, Vector2> TextSizes = new ();
		static readonly Dictionary<string, GUILayoutOption> TextWidthGUIOptions = new ();
		static readonly string[] FileSizeUnits = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
		static DirectoryInfo AssetsDirectoryInfo;
		static DirectoryInfo IsolationDirectoryInfo;
		static FileInfo ConfigPath;

		readonly Dictionary<string, string> ScriptGuidPaths = new ();
		readonly Dictionary<string, string> SceneGuidPaths = new ();
		Dictionary<string, string> FileGuidPaths;
		FileTreeView UnusedFileTreeView;
		Rect UnusedFileTreeViewRect;
		bool HasAnalysis;
		ConfigEntity Config = new ();

		[MenuItem ("Tools/Eruru/" + Name)]
		static void Open () {
			GetWindow<AssetCleaner> (NameGUIContent.text);
		}

		Vector2 GetTextSize (string text, GUIStyle style) {
			if (!TextSizes.TryGetValue (text, out var size)) {
				size = style.CalcSize (EditorGUIUtility.TrTempContent (text));
				TextSizes.Add (text, size);
			}
			return size;
		}

		Vector2 GetLabelSize (string text) {
			return GetTextSize (text, GUI.skin.label);
		}

		GUILayoutOption GetTextGUIWidth (string text, GUIStyle style, float margin = 0) {
			if (!TextWidthGUIOptions.TryGetValue (text, out var guiOption)) {
				guiOption = GUILayout.Width (GetTextSize (text, style).x + margin);
				TextWidthGUIOptions.Add (text, guiOption);
			}
			return guiOption;
		}

		GUILayoutOption GetLabelGUIWidth (string text) {
			return GetTextGUIWidth (text, GUI.skin.label);
		}

		GUILayoutOption GetSelectableTextGUIWidth (string text) {
			return GetTextGUIWidth (text, GUI.skin.label, 5);
		}

		GUILayoutOption GetButtonGUIWidth (string text) {
			return GetTextGUIWidth (text, GUI.skin.button);
		}

		void OnGUI () {
			if (FileGuidPaths == null) {
				FileGuidPaths = new ();
				switch (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToUpperInvariant ()) {
					case "ZH":
						NameGUIContent.text = "资产清理器";
						AnalysisGUIContent.text = "分析";
						CollectingFilesGUIContent.text = "阶段 {0}/{1} 收集文件中 {2}/{3}";
						CheckSceneDependenciesGUIContent.text = "阶段 {0}/{1} 检查场景 {2}/{3} 依赖中 {4}/{5}";
						CheckScriptDependenciesGUIContent.text = "阶段{0}/{1} 检查脚本依赖中 {2}/{3}";
						SummaryFilesGUIContent.text = "阶段 {0}/{1} 总结文件中 {2}/{3}";
						IsolationUnusedFilesGUIContent.text = "隔离未使用文件";
						MovingFilesGUIContent.text = "移动文件中";
						RestoreFilesGUIContent.text = "恢复文件";
						NoUnusedFileGUIContent.text = "无未使用文件";
						PleaseSaveSceneGUIContent.text = "请先保存所有当前场景";
						OkGUIContent.text = "确定";
						OpenIsolationDirectoryGUIContent.text = "打开隔离文件夹";
						break;
				}
				titleContent = NameGUIContent;
				AssetsDirectoryInfo = new DirectoryInfo (Application.dataPath);
				IsolationDirectoryInfo = new DirectoryInfo (Path.Combine (
					AssetsDirectoryInfo.Parent.FullName, "_Eruru", "Asset Cleaner", "Isolation"
				));
				ConfigPath = new FileInfo (Path.Combine (IsolationDirectoryInfo.Parent.FullName, "Config.json"));
				if (ConfigPath.Exists) {
					Config = JsonUtility.FromJson<ConfigEntity> (File.ReadAllText (ConfigPath.FullName));
				}
				UnusedFileTreeView = new (AssetsDirectoryInfo);
			}
			if (Config.HasIsolationFiles) {
				if (GUILayout.Button (RestoreFilesGUIContent.text)) {
					Restore ();
				}
				if (GUILayout.Button (OpenIsolationDirectoryGUIContent.text)) {
					EditorUtility.RevealInFinder (IsolationDirectoryInfo.FullName);
				}
				return;
			}
			if (GUILayout.Button (AnalysisGUIContent.text)) {
				Analysis ();
			}
			if (GUILayout.Button (OpenIsolationDirectoryGUIContent.text)) {
				EditorUtility.RevealInFinder (IsolationDirectoryInfo.FullName);
			}
			if (!HasAnalysis) {
				return;
			}
			if (FileGuidPaths.Count == 0) {
				EditorGUILayout.HelpBox (NoUnusedFileGUIContent.text, MessageType.Info);
				return;
			}
			if (GUILayout.Button (IsolationUnusedFilesGUIContent.text)) {
				Isolation ();
			}
			var rect = GUILayoutUtility.GetRect (
				GUIContent.none,
				GUIStyle.none,
				GUILayout.ExpandWidth (true),
				GUILayout.ExpandHeight (true)
			);
			UnusedFileTreeView.OnGUI (rect);
			if (UnusedFileTreeViewRect != rect) {
				UnusedFileTreeViewRect = rect;
				UnusedFileTreeView.multiColumnHeader.ResizeToFit ();
			}
		}

		void OnDestroy () {
			Clear ();
		}

		void Clear () {
			ScriptGuidPaths.Clear ();
			SceneGuidPaths.Clear ();
			FileGuidPaths.Clear ();
		}

		float ToProgress (int current, int total) {
			return total == 0 ? 0 : (float)current / total;
		}

		void Analysis () {
			if (Enumerable.Range (0, SceneManager.sceneCount).Any (x => SceneManager.GetSceneAt (x).isDirty)) {
				EditorUtility.DisplayDialog (NameGUIContent.text, PleaseSaveSceneGUIContent.text, OkGUIContent.text);
				return;
			}
			try {
				Clear ();
				var stage = 1;
				var totalStage = 4;
				var current = 0;
				var fileGuids = AssetDatabase.FindAssets ("", new string[] { "Assets" });
				FileGuidPaths = fileGuids
					.Select (static x => (Guid: x, Path: AssetDatabase.GUIDToAssetPath (x)))
					.Where (x => {
						current++;
						if (current <= 1 || current % 100 == 0) {
							EditorUtility.DisplayProgressBar (NameGUIContent.text, string.Format (
								CollectingFilesGUIContent.text, stage, totalStage, current, FileGuidPaths.Count
							), ToProgress (current, FileGuidPaths.Count));
						}
						if (AssetDatabase.IsValidFolder (x.Path)) {
							return false;
						}
						var fileType = AssetDatabase.GetMainAssetTypeAtPath (x.Path);
						if (fileType == typeof (MonoScript)) {
							ScriptGuidPaths.Add (x.Guid, x.Path);
							return false;
						}
						if (fileType == typeof (SceneAsset)) {
							SceneGuidPaths.Add (x.Guid, x.Path);
							return false;
						}
						return true;
					})
					.ToDictionary (static x => x.Guid, static x => x.Path);
				stage++;
				current = 0;
				var currentScene = 0;
				var total = 0;
				foreach (var sceneGuidPath in SceneGuidPaths) {
					currentScene++;
					var dependencyPaths = AssetDatabase.GetDependencies (sceneGuidPath.Value, true);
					total += dependencyPaths.Length;
					foreach (var dependencyPath in dependencyPaths) {
						current++;
						if (current <= 1 || current % 100 == 0) {
							EditorUtility.DisplayProgressBar (NameGUIContent.text, string.Format (
								CheckSceneDependenciesGUIContent.text,
								stage, totalStage, currentScene, SceneGuidPaths.Count, current, total
							), ToProgress (current, total));
						}
						var dependencyGuid = AssetDatabase.AssetPathToGUID (dependencyPath);
						FileGuidPaths.Remove (dependencyGuid);
					}
				}
				stage++;
				current = 0;
				var scriptGuids = ScriptGuidPaths.Keys.ToArray ();
				foreach (var scriptGuid in scriptGuids) {
					current++;
					if (current <= 1 || current % 100 == 0) {
						EditorUtility.DisplayProgressBar (NameGUIContent.text, string.Format (
							CheckScriptDependenciesGUIContent.text, stage, totalStage, current, scriptGuids.Length
						), ToProgress (current, scriptGuids.Length));
					}
					if (!ScriptGuidPaths.TryGetValue (scriptGuid, out var scriptPath)) {
						continue;
					}
					ScriptGuidPaths.Remove (scriptGuid);
					var scriptFileInfo = new FileInfo (Path.Combine (AssetsDirectoryInfo.Parent.FullName, scriptPath));
					var scriptRootDirectoryInfo = scriptFileInfo.Directory;
					DirectoryInfo lastScriptRootDirectoryInfo = null;
					while (!string.Equals (scriptRootDirectoryInfo.Name, AssetsDirectoryInfo.Name, StringComparison.OrdinalIgnoreCase)) {
						lastScriptRootDirectoryInfo = scriptRootDirectoryInfo;
						scriptRootDirectoryInfo = scriptRootDirectoryInfo.Parent;
					}
					scriptRootDirectoryInfo = lastScriptRootDirectoryInfo;
					if (scriptRootDirectoryInfo == null) {
						continue;
					}
					foreach (var fileInfo in scriptRootDirectoryInfo.GetFiles ("*", SearchOption.AllDirectories)) {
						var filePath = Path.GetRelativePath (AssetsDirectoryInfo.Parent.FullName, fileInfo.FullName);
						var fileGuid = AssetDatabase.AssetPathToGUID (filePath, AssetPathToGUIDOptions.OnlyExistingAssets);
						if (string.IsNullOrEmpty (fileGuid)) {
							continue;
						}
						var fileType = AssetDatabase.GetMainAssetTypeAtPath (filePath);
						if (fileType == typeof (MonoScript)) {
							ScriptGuidPaths.Remove (fileGuid);
						}
						FileGuidPaths.Remove (fileGuid);
					}
				}
				stage++;
				current = 0;
				foreach (var fileGuidPath in FileGuidPaths) {
					current++;
					if (current <= 1 || current % 100 == 0) {
						EditorUtility.DisplayProgressBar (NameGUIContent.text, string.Format (
							SummaryFilesGUIContent.text, stage, totalStage, current, FileGuidPaths.Count
						), ToProgress (current, FileGuidPaths.Count));
					}
				}
				UnusedFileTreeView.Reload (FileGuidPaths);
				HasAnalysis = true;
			} finally {
				EditorUtility.ClearProgressBar ();
			}
		}

		void SaveConfig () {
			ConfigPath.Directory.Create ();
			File.WriteAllText (ConfigPath.FullName, JsonUtility.ToJson (Config, true));
		}

		void Isolation () {
			Move (true, FileGuidPaths.Values.ToArray ());
			Config.HasIsolationFiles = true;
			SaveConfig ();
		}

		void Restore () {
			Move (false, IsolationDirectoryInfo.GetFiles ("*", SearchOption.AllDirectories)
				.Select (x => Path.GetRelativePath (IsolationDirectoryInfo.FullName, x.FullName))
				.ToArray ());
			Config.HasIsolationFiles = false;
			SaveConfig ();
		}

		void Move (bool isIsolation, string[] filePaths) {
			AssetDatabase.StartAssetEditing ();
			try {
				var projectDirectoryInfo = AssetsDirectoryInfo.Parent;
				var isolationDirectoryInfo = IsolationDirectoryInfo;
				if (!isIsolation) {
					(projectDirectoryInfo, isolationDirectoryInfo) = (isolationDirectoryInfo, projectDirectoryInfo);
				}
				var current = 0;
				foreach (var filePath in filePaths) {
					current++;
					if (current <= 1 || current % 100 == 0) {
						EditorUtility.DisplayProgressBar (NameGUIContent.text, string.Format (
							MovingFilesGUIContent.text
						), ToProgress (current, filePaths.Length));
					}
					Move (projectDirectoryInfo, filePath, isolationDirectoryInfo, isIsolation);
					if (!isIsolation) {
						continue;
					}
					Move (projectDirectoryInfo, AssetDatabase.GetTextMetaFilePathFromAssetPath (filePath), isolationDirectoryInfo, isIsolation);
				}
			} finally {
				EditorUtility.ClearProgressBar ();
				AssetDatabase.StopAssetEditing ();
				AssetDatabase.Refresh ();
			}
		}

		void Move (DirectoryInfo source, string relativePath, DirectoryInfo target, bool isIsolation) {
			var fileInfo = new FileInfo (Path.Combine (source.FullName, relativePath));
			if (!fileInfo.Exists) {
				return;
			}
			var newFileInfo = new FileInfo (Path.Combine (target.FullName, relativePath));
			if (newFileInfo.Exists) {
				if (!isIsolation) {
					return;
				}
				newFileInfo.Delete ();
			}
			newFileInfo.Directory.Create ();
			fileInfo.MoveTo (newFileInfo.FullName);
		}

		static string GetFileSizeText (double length, string format = "{0:0.##} {1}") {
			var index = 0;
			while (length >= 1024) {
				length /= 1024;
				index++;
			}
			return string.Format (format, length, FileSizeUnits[index]);
		}

		[Serializable]
		class ConfigEntity {

			public bool HasIsolationFiles;

		}

		class FileTreeView : TreeView {

			static readonly GUIContent NameGUIContent = new ("Unused Files");
			static readonly GUIContent SizeGUIContent = new ("Size");
			static readonly GUIContent FileCountGUIContent = new ("Files");

			readonly DirectoryInfo AssetsDirectoryInfo;
			Dictionary<string, string> FileGuidPaths;

			public FileTreeView (DirectoryInfo assetsDirectoryInfo) : base (new (), CreateHeader ()) {
				AssetsDirectoryInfo = assetsDirectoryInfo;
				Reload ();
			}

			static MultiColumnHeader CreateHeader () {
				switch (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToUpperInvariant ()) {
					case "ZH":
						NameGUIContent.text = "未使用文件";
						SizeGUIContent.text = "大小";
						FileCountGUIContent.text = "文件数";
						break;
				}
				return new MultiColumnHeader (new MultiColumnHeaderState (new MultiColumnHeaderState.Column[] {
					new () {
						headerContent = NameGUIContent,
						autoResize = true,
						canSort = false
					},
					new () {
						headerContent = SizeGUIContent,
						autoResize = false,
						canSort = false,
						width = 100
					},
					new () {
						headerContent = FileCountGUIContent,
						autoResize= false,
						canSort = false,
						width = 100
					}
				}));
			}

			public void Reload (Dictionary<string, string> fileGuidPaths) {
				FileGuidPaths = fileGuidPaths;
				Reload ();
			}

			protected override TreeViewItem BuildRoot () {
				var id = 0;
				var root = new FileTreeViewItem (id, -1, AssetsDirectoryInfo.Parent.Name, true, string.Empty) {
					children = new (), icon = AssetDatabase.GetCachedIcon (AssetsDirectoryInfo.Name) as Texture2D
				};
				if (FileGuidPaths != null) {
					foreach (var fileGuidPath in FileGuidPaths) {
						var filePathParts = fileGuidPath.Value.Split ('/');
						var i = -1;
						var parentDirectory = root;
						var fileInfo = new FileInfo (Path.Combine (AssetsDirectoryInfo.Parent.FullName, fileGuidPath.Value));
						foreach (var filePathPart in filePathParts) {
							i++;
							var isDirectory = i < filePathParts.Length - 1;
							if (isDirectory) {
								if (!parentDirectory.NameDirectories.TryGetValue (filePathPart, out var directory)) {
									id++;
									directory = new (id, parentDirectory.depth + 1, filePathPart, isDirectory, fileGuidPath.Key) {
										icon = root.icon
									};
									parentDirectory.NameDirectories.Add (filePathPart, directory);
									parentDirectory.AddChild (directory);
								}
								parentDirectory = directory;
								directory.Size += fileInfo.Length;
								directory.FileCount++;
								continue;
							}
							id++;
							parentDirectory.AddChild (new FileTreeViewItem (id, parentDirectory.depth + 1, filePathPart, false, fileGuidPath.Key) {
								Size = fileInfo.Length, icon = AssetDatabase.GetCachedIcon (fileGuidPath.Value) as Texture2D
							});
						}
					}
					Sort (root);
					SetupDepthsFromParentsAndChildren (root);
					multiColumnHeader.ResizeToFit ();
					SetExpanded (root.id + 1, true);
				}
				return root;
			}

			protected override void RowGUI (RowGUIArgs args) {
				var item = (FileTreeViewItem)args.item;
				for (var i = 0; i < args.GetNumVisibleColumns (); i++) {
					var cellRect = args.GetCellRect (i);
					CenterRectUsingSingleLineHeight (ref cellRect);
					switch (i) {
						case 0:
							base.RowGUI (args);
							break;
						case 1:
							EditorGUI.LabelField (cellRect, item.SizeText);
							break;
						case 2:
							if (!item.IsDirectory) {
								break;
							}
							EditorGUI.LabelField (cellRect, item.FileCountText);
							break;
					}
				}
			}

			protected override void DoubleClickedItem (int id) {
				if (FindItem (id, rootItem) is not FileTreeViewItem item) {
					return;
				}
				if (item.IsDirectory) {
					SetExpanded (id, !IsExpanded (id));
					return;
				}
				EditorGUIUtility.PingObject (AssetDatabase.LoadMainAssetAtPath (AssetDatabase.GUIDToAssetPath (item.Guid)));
			}

			static void Sort (FileTreeViewItem directory) {
				directory.children.Sort (static (a, b) => {
					var x = (FileTreeViewItem)a;
					var y = (FileTreeViewItem)b;
					var result = 0;
					if (result == 0) {
						result = x.IsDirectory.CompareTo (y.IsDirectory) * -1;
					}
					if (result == 0) {
						result = x.Size.CompareTo (y.Size) * -1;
					}
					if (result == 0) {
						result = x.displayName.CompareTo (y.displayName);
					}
					return result;
				});
				foreach (var children in directory.children) {
					((FileTreeViewItem)children).RefreshText ();
				}
				foreach (var nameDirectory in directory.NameDirectories) {
					Sort (nameDirectory.Value);
				}
			}

		}

		class FileTreeViewItem : TreeViewItem {

			public bool IsDirectory { get; set; }
			public long Size { get; set; }
			public int FileCount { get; set; }
			public string Guid { get; set; } = string.Empty;
			public Dictionary<string, FileTreeViewItem> NameDirectories = new ();
			public string SizeText { get; set; } = string.Empty;
			public string FileCountText { get; set; } = string.Empty;

			public FileTreeViewItem (
				int id, int depth, string displayName, bool isDirectory, string guid
			) : base (id, depth, displayName) {
				IsDirectory = isDirectory;
				Guid = guid;
			}

			public void RefreshText () {
				SizeText = GetFileSizeText (Size);
				FileCountText = FileCount.ToString ();
			}

		}

	}

}
#endif