
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.BatchRenamer
{
    public class MegaBatchRenamer : EditorWindow
    {
        private const string MarkerOriginal = "$&";

        private static readonly Regex IncrementerRegex = new Regex(@"\$([nN]+)", RegexOptions.Compiled);

        private static readonly Regex ConditionalTypeRegex = new Regex(@"\$\?([A-Za-z0-9_]+)\?", RegexOptions.Compiled);

        private static readonly Regex CamelCaseWordRegex = new Regex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.Compiled);

        private const int InputFontSize = 16;

        private const int InputHeight = 26;

        private static readonly Color ActiveToggleTextColor = new Color(0.56f, 0.73f, 0.98f, 1f);

        private enum QuickCaseAction
        {
            None,
            Upper,
            Lower,
            Camel,
            InverseCamel
        }

        private enum TargetMode
        {
            SceneGameObjects,
            ProjectFiles
        }

        private string mFind = string.Empty;

        private string mRename = string.Empty;

        private bool mCaseSensitive;

        private int mStartIndex = 1;

        private QuickCaseAction mQuickCaseAction = QuickCaseAction.None;

        private bool mFilterEnabled;

        private bool mRenameNested = true;

        private readonly List<TypeFilterEntry> mTypeFilters = new()
        {
            new TypeFilterEntry("MeshRenderer",        typeof(MeshRenderer), "mesh"),
            new TypeFilterEntry("SkinnedMeshRenderer", typeof(SkinnedMeshRenderer), "skin"),
            new TypeFilterEntry("Light",               typeof(Light), "light"),
            new TypeFilterEntry("Camera",              typeof(Camera), "cam"),
            new TypeFilterEntry("Collider",            typeof(Collider), "collider"),
            new TypeFilterEntry("Rigidbody",           typeof(Rigidbody), "rbody"),
            new TypeFilterEntry("Canvas",              typeof(Canvas), "canvas"),
            new TypeFilterEntry("RectTransform",       typeof(RectTransform), "rect"),
            new TypeFilterEntry("AudioSource",         typeof(AudioSource), "audio"),
            new TypeFilterEntry("ParticleSystem",      typeof(ParticleSystem), "particle"),
            new TypeFilterEntry("Animator",            typeof(Animator), "animator"),
        };

        private readonly List<ProjectFilterEntry> mProjectFilters = new()
        {
            new ProjectFilterEntry("Folders", "folder", true),
            new ProjectFilterEntry("Prefab", "prefab", ".prefab"),
            new ProjectFilterEntry("Scene", "scene", ".unity"),
            new ProjectFilterEntry("Material", "mat", ".mat"),
            new ProjectFilterEntry("Script", "cs", ".cs"),
            new ProjectFilterEntry("Texture", "tex", ".png", ".jpg", ".jpeg", ".tga", ".psd"),
            new ProjectFilterEntry("Model", "model", ".fbx", ".obj"),
            new ProjectFilterEntry("Animation", "anim", ".anim", ".controller"),
            new ProjectFilterEntry("Audio", "audio", ".wav", ".mp3", ".ogg"),
        };

        private readonly List<RenameTarget> mPreviewTargets = new();

        private readonly List<string> mPreviewNewNames = new();

        private readonly List<PreviewRow> mPreviewRows = new();

        private TextField mFindField;
        private TextField mRenameField;
        private Toggle mCaseSensitiveToggle;
        private Toggle mRenameNestedToggle;
        private IntegerField mStartField;
        private Toggle mFilterEnabledToggle;
        private Label mPreviewLabel;
        private Button mRenameButton;
        private ListView mPreviewList;
        private Foldout mFilterFoldout;
        private VisualElement mFilterContent;
        private VisualElement mFilterButtonsWrap;
        private Button mFilterAllButton;
        private Button mFilterNoneButton;
        private Button mUpperActionButton;
        private Button mLowerActionButton;
        private Button mCamelActionButton;
        private Button mInvertCamelActionButton;
        private Label mTitleLabel;
        private TargetMode mTargetMode;
        private TargetMode mFilterButtonsMode;
        private bool mIsPreviewRebuildQueued;

        private IReadOnlyList<FilterEntryBase> ActiveFilters =>
            mTargetMode == TargetMode.ProjectFiles
                ? (IReadOnlyList<FilterEntryBase>)mProjectFilters
                : mTypeFilters;

        #region Window and UI

        [MenuItem("Tools/BatchRenamer")]
        public static void Open()
        {
            var win = GetWindow<MegaBatchRenamer>("Batch Renamer");
            win.minSize = new Vector2(520, 520);
            win.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            mIsPreviewRebuildQueued = false;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            mTitleLabel = new Label();
            mTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            mTitleLabel.style.fontSize = 15;
            mTitleLabel.style.marginBottom = 6;
            root.Add(mTitleLabel);

            var info = new HelpBox(
                "Переименовывает выделенные GameObject или assets (в зависимости от активного окна).\n" +
                "Токены в поле Rename:  $& = исходное имя,  $n / $nn / $nnn ... = счётчик с заданным числом разрядов,\n" +
                "$TT = все типы на объекте из списка фильтров,  $?Type? = вставка типа только если компонент есть.",
                HelpBoxMessageType.None);
            info.style.marginBottom = 8;
            root.Add(info);

            BuildMainFields(root);
            BuildQuickInsertButtons(root);
            BuildQuickCaseButtons(root);
            BuildOptions(root);
            BuildFilterBlock(root);
            BuildPreview(root);
            BuildActionBlock(root);

            UpdateTargetMode();
            RebuildPreviewAndRefreshUi();
        }

        private void BuildMainFields(VisualElement root)
        {
            mFindField = new TextField("Find in name:") { value = mFind };
            TuneInputField(mFindField);
            mFindField.RegisterValueChangedCallback(evt =>
            {
                mFind = evt.newValue ?? string.Empty;
                RebuildPreviewAndRefreshUi();
            });
            root.Add(mFindField);

            mRenameField = new TextField("Replace with:") { value = mRename };
            TuneInputField(mRenameField);
            mRenameField.RegisterValueChangedCallback(evt =>
            {
                mRename = evt.newValue ?? string.Empty;
                RebuildPreviewAndRefreshUi();
            });
            root.Add(mRenameField);
        }

        private void BuildQuickInsertButtons(VisualElement root)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 8;

            var currentNameButton = new Button(() => InsertTokenIntoRename("$&"))
            {
                text = "Current name"
            };

            var upButton = new Button(() =>
            {
                InsertTokenIntoRename("$nn");
            })
            {
                text = "Number ↑"
            };

            var downButton = new Button(() =>
            {
                InsertTokenIntoRename("$NN");
            })
            {
                text = "Number ↓"
            };

            var typesButton = new Button(() => InsertTokenIntoRename("$TT"))
            {
                text = "Types $TT"
            };

            Button conditionalButton = null;
            conditionalButton = new Button(() => ShowConditionalTokenMenu(conditionalButton))
            {
                text = "If type $?"
            };

            currentNameButton.style.marginRight = 6;
            upButton.style.marginRight = 6;
            downButton.style.marginRight = 6;
            typesButton.style.marginRight = 6;

            row.Add(currentNameButton);
            row.Add(upButton);
            row.Add(downButton);
            row.Add(typesButton);
            row.Add(conditionalButton);
            root.Add(row);
        }

        private void ShowConditionalTokenMenu(Button anchor)
        {
            var menu = new GenericMenu();

            if (mTargetMode == TargetMode.ProjectFiles)
            {
                for (int i = 0; i < mProjectFilters.Count; i++)
                {
                    var entry = mProjectFilters[i];
                    var token = "$?" + entry.Token + "?";
                    var label = entry.Label + "  (" + token + ")";
                    menu.AddItem(new GUIContent(label), false, () => InsertTokenIntoRename(token));
                }
            }
            else
            {
                foreach (var entry in mTypeFilters)
                {
                    var token = "$?" + entry.Token + "?";
                    var label = entry.Label + "  (" + token + ")";
                    menu.AddItem(new GUIContent(label), false, () => InsertTokenIntoRename(token));
                }
            }

            var dropRect = anchor != null ? anchor.worldBound : new Rect(0, 0, 1, 1);
            menu.DropDown(dropRect);
        }

        private void BuildQuickCaseButtons(VisualElement root)
        {
            var label = new Label("Quick actions");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 2;
            root.Add(label);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 8;

            mUpperActionButton = new Button(() => SetQuickCaseAction(QuickCaseAction.Upper))
            {
                text = "UPPER"
            };

            mLowerActionButton = new Button(() => SetQuickCaseAction(QuickCaseAction.Lower))
            {
                text = "lower"
            };

            mCamelActionButton = new Button(() => SetQuickCaseAction(QuickCaseAction.Camel))
            {
                text = "camelCase"
            };

            mInvertCamelActionButton = new Button(() => SetQuickCaseAction(QuickCaseAction.InverseCamel))
            {
                text = "camel_Case"
            };

            mUpperActionButton.style.marginRight = 6;
            mLowerActionButton.style.marginRight = 6;
            mCamelActionButton.style.marginRight = 6;

            row.Add(mUpperActionButton);
            row.Add(mLowerActionButton);
            row.Add(mCamelActionButton);
            row.Add(mInvertCamelActionButton);
            root.Add(row);

            UpdateQuickCaseButtonsState();
        }

        private void SetQuickCaseAction(QuickCaseAction action)
        {
            mQuickCaseAction = mQuickCaseAction == action ? QuickCaseAction.None : action;
            UpdateQuickCaseButtonsState();
            RebuildPreviewAndRefreshUi();
        }

        private void UpdateQuickCaseButtonsState()
        {
            if (mUpperActionButton == null || mLowerActionButton == null || mCamelActionButton == null || mInvertCamelActionButton == null)
                return;

            SetButtonActive(mUpperActionButton, mQuickCaseAction == QuickCaseAction.Upper);
            SetButtonActive(mLowerActionButton, mQuickCaseAction == QuickCaseAction.Lower);
            SetButtonActive(mCamelActionButton, mQuickCaseAction == QuickCaseAction.Camel);
            SetButtonActive(mInvertCamelActionButton, mQuickCaseAction == QuickCaseAction.InverseCamel);
        }

        private static void SetButtonActive(Button button, bool active)
        {
            button.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            button.style.opacity = active ? 1f : 0.85f;
            button.style.color = active ? new StyleColor(ActiveToggleTextColor) : new StyleColor(StyleKeyword.Null);
        }

        private void BuildOptions(VisualElement root)
        {
            var optionsRow = new VisualElement();
            optionsRow.style.flexDirection = FlexDirection.Row;
            optionsRow.style.alignItems = Align.Center;
            optionsRow.style.marginBottom = 6;

            var caseSensitiveGroup = CreateInlineLabeledToggle("Case Sensitive", mCaseSensitive, out mCaseSensitiveToggle);
            mCaseSensitiveToggle.RegisterValueChangedCallback(evt =>
            {
                mCaseSensitive = evt.newValue;
                RebuildPreviewAndRefreshUi();
            });
            optionsRow.Add(caseSensitiveGroup);

            var renameNestedGroup = CreateInlineLabeledToggle("Rename Nested", mRenameNested, out mRenameNestedToggle);
            mRenameNestedToggle.RegisterValueChangedCallback(evt =>
            {
                mRenameNested = evt.newValue;
                RebuildPreviewAndRefreshUi();
            });
            optionsRow.Add(renameNestedGroup);

            mStartField = new IntegerField("Start index");
            mStartField.value = mStartIndex;
            mStartField.style.width = StyleKeyword.Auto;
            mStartField.style.flexShrink = 0;
            if (mStartField.labelElement != null)
            {
                mStartField.labelElement.style.minWidth = StyleKeyword.Auto;
                mStartField.labelElement.style.width = StyleKeyword.Auto;
                mStartField.labelElement.style.flexBasis = StyleKeyword.Auto;
                mStartField.labelElement.style.flexShrink = 0;
            }

            var startInput = mStartField.Q(className: "unity-base-text-field__input") ??
                                       mStartField.Q(className: "unity-text-input");
            if (startInput != null)
            {
                startInput.style.minWidth = StyleKeyword.Auto;
                startInput.style.width = StyleKeyword.Auto;
                startInput.style.flexBasis = StyleKeyword.Auto;
                startInput.style.flexGrow = 0;
            }

            mStartField.RegisterValueChangedCallback(evt =>
            {
                mStartIndex = evt.newValue;
                RebuildPreviewAndRefreshUi();
            });
            optionsRow.Add(mStartField);

            root.Add(optionsRow);

            var spacer = new VisualElement();
            spacer.style.height = 6;
            root.Add(spacer);
        }

        private static VisualElement CreateInlineLabeledToggle(string labelText, bool value, out Toggle toggle)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.alignItems = Align.Center;
            group.style.marginRight = 12;

            var label = new Label(labelText);
            label.style.marginRight = 2;
            group.Add(label);

            toggle = new Toggle(string.Empty) { value = value };
            group.Add(toggle);

            return group;
        }

        private void BuildFilterBlock(VisualElement root)
        {
            mFilterEnabledToggle = new Toggle("Filter by Component Type") { value = mFilterEnabled };
            mFilterEnabledToggle.style.unityFontStyleAndWeight = FontStyle.Bold;
            mFilterEnabledToggle.RegisterValueChangedCallback(evt =>
            {
                mFilterEnabled = evt.newValue;
                RefreshFilterEnabledState();
                RebuildPreviewAndRefreshUi();
            });
            root.Add(mFilterEnabledToggle);

            mFilterFoldout = new Foldout
            {
                text = "Type filters",
                value = true
            };
            mFilterFoldout.style.marginBottom = 4;
            root.Add(mFilterFoldout);

            mFilterContent = new VisualElement();
            mFilterFoldout.Add(mFilterContent);

            var controlsRow = new VisualElement();
            controlsRow.style.flexDirection = FlexDirection.Row;
            controlsRow.style.marginBottom = 4;

            mFilterAllButton = new Button(() =>
            {
                var filters = ActiveFilters;
                for (int i = 0; i < filters.Count; i++)
                {
                    filters[i].Enabled = true;
                    UpdateFilterButtonVisual(filters[i]);
                }
                RebuildPreviewAndRefreshUi();
            })
            {
                text = "All"
            };

            mFilterNoneButton = new Button(() =>
            {
                var filters = ActiveFilters;
                for (int i = 0; i < filters.Count; i++)
                {
                    filters[i].Enabled = false;
                    UpdateFilterButtonVisual(filters[i]);
                }
                RebuildPreviewAndRefreshUi();
            })
            {
                text = "None"
            };

            mFilterAllButton.style.width = 70;
            mFilterNoneButton.style.width = 70;
            mFilterAllButton.style.marginRight = 6;

            controlsRow.Add(mFilterAllButton);
            controlsRow.Add(mFilterNoneButton);
            mFilterContent.Add(controlsRow);

            mFilterButtonsWrap = new VisualElement();
            mFilterButtonsWrap.style.flexDirection = FlexDirection.Row;
            mFilterButtonsWrap.style.flexWrap = Wrap.Wrap;
            mFilterButtonsWrap.style.marginBottom = 2;
            mFilterContent.Add(mFilterButtonsWrap);

            RebuildActiveFilterButtons();

            var spacer = new VisualElement();
            spacer.style.height = 6;
            root.Add(spacer);

            RefreshFilterEnabledState();
        }

        private void AddFilterToggleToRow(VisualElement row, FilterEntryBase entry)
        {
            var button = new Button(() =>
            {
                entry.Enabled = !entry.Enabled;
                UpdateFilterButtonVisual(entry);
                RebuildPreviewAndRefreshUi();
            })
            {
                text = entry.Label
            };

            button.style.flexGrow = 0;
            button.style.flexShrink = 0;
            button.style.marginBottom = 6;
            button.style.marginRight = 6;

            entry.UiButton = button;
            UpdateFilterButtonVisual(entry);
            row.Add(button);
        }

        private void UpdateFilterButtonVisual(FilterEntryBase entry)
        {
            if (entry == null || entry.UiButton == null) return;
            SetButtonActive(entry.UiButton, entry.Enabled);
        }

        private void RebuildActiveFilterButtons()
        {
            if (mFilterButtonsWrap == null) return;

            mFilterButtonsWrap.Clear();
            mFilterButtonsMode = mTargetMode;

            var filters = ActiveFilters;
            for (int i = 0; i < filters.Count; i++)
            {
                filters[i].UiButton = null;
                AddFilterToggleToRow(mFilterButtonsWrap, filters[i]);
            }
        }

        private static bool IsProjectBrowserFocused()
        {
            var focused = EditorWindow.focusedWindow;
            return focused != null && focused.GetType().Name == "ProjectBrowser";
        }

        private void UpdateTargetMode()
        {
            var hasProjectSelection = HasProjectAssetSelection();
            var useProject = hasProjectSelection || IsProjectBrowserFocused();
            mTargetMode = useProject ? TargetMode.ProjectFiles : TargetMode.SceneGameObjects;

            var title = mTargetMode == TargetMode.ProjectFiles
                ? "Batch Renamer - Project Files"
                : "Batch Renamer - Game Objects";

            if (mTitleLabel != null)
                mTitleLabel.text = title;

            titleContent = new GUIContent(title);

            if (mFilterEnabledToggle != null)
                mFilterEnabledToggle.label = mTargetMode == TargetMode.ProjectFiles
                    ? "Filter by Asset Type"
                    : "Filter by Component Type";

            if (mFilterFoldout != null)
                mFilterFoldout.text = mTargetMode == TargetMode.ProjectFiles
                    ? "Asset filters"
                    : "Type filters";
        }

        private static bool HasProjectAssetSelection()
        {
            var selectedAssets = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
            if (selectedAssets == null || selectedAssets.Length == 0)
                return false;

            for (int i = 0; i < selectedAssets.Length; i++)
            {
                if (TryGetAssetPath(selectedAssets[i], out _))
                    return true;
            }

            return false;
        }

        private static List<string> GetSelectedProjectPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var selectedAssets = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
            if (selectedAssets == null)
                return new List<string>(paths);

            for (int i = 0; i < selectedAssets.Length; i++)
            {
                if (TryGetAssetPath(selectedAssets[i], out string path))
                    paths.Add(path);
            }

            return new List<string>(paths);
        }

        private static bool TryGetAssetPath(UnityEngine.Object asset, out string path)
        {
            path = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path);
        }

        private void BuildActionBlock(VisualElement root)
        {
            mRenameButton = new Button(ApplyRename)
            {
                text = "Rename 0 object(s)"
            };
            mRenameButton.style.height = 28;
            mRenameButton.style.marginTop = 8;
            mRenameButton.style.marginBottom = 2;
            root.Add(mRenameButton);
        }

        private void BuildPreview(VisualElement root)
        {
            mPreviewLabel = new Label("Preview (0)");
            mPreviewLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            mPreviewLabel.style.marginBottom = 4;
            root.Add(mPreviewLabel);

            mPreviewList = new ListView
            {
                itemsSource = mPreviewRows,
                fixedItemHeight = 22,
                selectionType = SelectionType.None,
                style =
                {
                    flexGrow = 1,
                    minHeight = 180
                }
            };

            mPreviewList.makeItem = MakePreviewItem;
            mPreviewList.bindItem = BindPreviewItem;

            root.Add(mPreviewList);
        }

        private static VisualElement MakePreviewItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var objectField = new ObjectField
            {
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = true,
                name = "go"
            };
            objectField.SetEnabled(false);
            objectField.style.width = 240;
            objectField.style.marginRight = 4;

            var arrow = new Label("→");
            arrow.style.width = 16;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;

            var newName = new Label { name = "newName" };
            newName.style.flexGrow = 1;

            row.Add(objectField);
            row.Add(arrow);
            row.Add(newName);
            return row;
        }

        private void BindPreviewItem(VisualElement element, int index)
        {
            if (index < 0 || index >= mPreviewRows.Count) return;

            var row = mPreviewRows[index];
            var objectField = element.Q<ObjectField>("go");
            var newName = element.Q<Label>("newName");

            if (objectField != null)
                objectField.value = row.Target;

            if (newName != null)
                newName.text = row.NewName ?? string.Empty;
        }

        private void RefreshFilterEnabledState()
        {
            if (mFilterFoldout != null)
            {
                mFilterFoldout.style.display = mFilterEnabled ? DisplayStyle.Flex : DisplayStyle.None;
                mFilterFoldout.SetEnabled(mFilterEnabled);
            }

            if (mFilterContent != null)
                mFilterContent.SetEnabled(mFilterEnabled);

            if (mFilterAllButton != null)
                mFilterAllButton.SetEnabled(mFilterEnabled);

            if (mFilterNoneButton != null)
                mFilterNoneButton.SetEnabled(mFilterEnabled);

            var filters = ActiveFilters;
            for (int i = 0; i < filters.Count; i++)
            {
                if (filters[i].UiButton != null)
                    filters[i].UiButton.SetEnabled(mFilterEnabled);
            }
        }

        private void OnSelectionChanged()
        {
            RebuildPreviewAndRefreshUi();
        }

        private void InsertTokenIntoRename(string token)
        {
            if (mRenameField == null || string.IsNullOrEmpty(token)) return;

            var current = mRenameField.value ?? string.Empty;
            int a;
            int b;

            if (TryGetRenameSelectionRange(out int min, out int max))
            {
                a = Mathf.Clamp(min, 0, current.Length);
                b = Mathf.Clamp(max, 0, current.Length);
            }
            else
            {
                a = current.Length;
                b = current.Length;
            }

            var before = current.Substring(0, a);
            var after = current.Substring(b);
            var updated = before + token + after;
            var newCaret = a + token.Length;

            mRename = updated;
            mRenameField.SetValueWithoutNotify(updated);
            mRenameField.Focus();

            mRenameField.schedule.Execute(() =>
            {
                TrySetTextFieldSelectionRange(mRenameField, newCaret, newCaret);
            });

            RebuildPreviewAndRefreshUi();
        }

        #endregion

        #region TextField Selection Reflection

        private static bool TryGetRenameSelectionRange(TextField field, out int min, out int max)
        {
            min = 0;
            max = 0;

            if (field == null) return false;
            if (!TryGetTextFieldSelectionRange(field, out int cursor, out int select)) return false;

            min = Mathf.Min(cursor, select);
            max = Mathf.Max(cursor, select);
            return true;
        }

        private static bool TryGetTextFieldSelectionRange(TextField field, out int cursor, out int select)
        {
            cursor = 0;
            select = 0;

            var type = field.GetType();
            var cursorProp = type.GetProperty("cursorIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var selectProp = type.GetProperty("selectIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (cursorProp != null && selectProp != null)
            {
                var c = cursorProp.GetValue(field);
                var s = selectProp.GetValue(field);
                if (c is int ci && s is int si)
                {
                    cursor = ci;
                    select = si;
                    return true;
                }
            }

            var selectionObjects = GetTextFieldSelectionObjects(field, type);
            for (int i = 0; i < selectionObjects.Length; i++)
            {
                if (TryGetSelectionFromObject(selectionObjects[i], out cursor, out select))
                    return true;
            }

            return false;
        }

        private static bool TrySetTextFieldSelectionRange(TextField field, int cursor, int select)
        {
            if (field == null) return false;

            var type = field.GetType();
            var cursorProp = type.GetProperty("cursorIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var selectProp = type.GetProperty("selectIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            bool setDirect = false;
            if (cursorProp != null && cursorProp.CanWrite)
            {
                cursorProp.SetValue(field, cursor);
                setDirect = true;
            }
            if (selectProp != null && selectProp.CanWrite)
            {
                selectProp.SetValue(field, select);
                setDirect = true;
            }
            if (setDirect) return true;

            var selectionObjects = GetTextFieldSelectionObjects(field, type);
            for (int i = 0; i < selectionObjects.Length; i++)
            {
                if (TrySetSelectionOnObject(selectionObjects[i], cursor, select))
                    return true;
            }

            return false;
        }

        private static object[] GetTextFieldSelectionObjects(TextField field, Type type)
        {
            // In different Unity versions, indices can be exposed on different nested members.
            var textSelectionProp = type.GetProperty("textSelection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var textSelection = textSelectionProp?.GetValue(field);
            var textInputBase = GetMemberValue(field, "textInputBase");
            var textEdition = GetMemberValue(textInputBase, "textEdition");
            var nestedSelection = GetMemberValue(textInputBase, "textSelection");

            return new object[]
            {
                field,
                textSelection,
                textInputBase,
                textEdition,
                nestedSelection
            };
        }

        private static bool TryGetSelectionFromObject(object obj, out int cursor, out int select)
        {
            cursor = 0;
            select = 0;
            if (obj == null) return false;

            var t = obj.GetType();

            if (TryGetIntMember(t, obj, "cursorIndex", out cursor) &&
                TryGetIntMember(t, obj, "selectIndex", out select))
                return true;

            if (TryGetIntMember(t, obj, "m_CursorIndex", out cursor) &&
                TryGetIntMember(t, obj, "m_SelectIndex", out select))
                return true;

            return false;
        }

        private static bool TrySetSelectionOnObject(object obj, int cursor, int select)
        {
            if (obj == null) return false;

            var t = obj.GetType();
            bool wroteAny = false;

            wroteAny |= TrySetIntMember(t, obj, "cursorIndex", cursor);
            wroteAny |= TrySetIntMember(t, obj, "selectIndex", select);

            if (wroteAny) return true;

            wroteAny |= TrySetIntMember(t, obj, "m_CursorIndex", cursor);
            wroteAny |= TrySetIntMember(t, obj, "m_SelectIndex", select);
            return wroteAny;
        }

        private static object GetMemberValue(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrEmpty(memberName)) return null;

            var t = obj.GetType();
            TryGetMember(t, memberName, out var prop, out var field);

            if (prop != null)
                return prop.GetValue(obj);

            return field?.GetValue(obj);
        }

        private static bool TryGetIntMember(Type t, object obj, string memberName, out int value)
        {
            value = 0;
            if (t == null || obj == null) return false;

            TryGetMember(t, memberName, out var prop, out var field);
            if (prop != null)
            {
                var raw = prop.GetValue(obj);
                if (raw is int i)
                {
                    value = i;
                    return true;
                }
            }

            if (field != null)
            {
                var raw = field.GetValue(obj);
                if (raw is int i)
                {
                    value = i;
                    return true;
                }
            }

            return false;
        }

        private static bool TrySetIntMember(Type t, object obj, string memberName, int value)
        {
            if (t == null || obj == null) return false;

            TryGetMember(t, memberName, out var prop, out var field);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value);
                return true;
            }

            if (field != null)
            {
                field.SetValue(obj, value);
                return true;
            }

            return false;
        }

        private static void TryGetMember(Type t, string memberName, out PropertyInfo prop, out FieldInfo field)
        {
            prop = null;
            field = null;

            if (t == null || string.IsNullOrEmpty(memberName))
                return;

            prop = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
                return;

            field = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private bool TryGetRenameSelectionRange(out int min, out int max)
        {
            return TryGetRenameSelectionRange(mRenameField, out min, out max);
        }

        #endregion

        #region Preview and Target Collection

        private void RebuildPreviewAndRefreshUi()
        {
            if (mIsPreviewRebuildQueued)
                return;

            mIsPreviewRebuildQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                mIsPreviewRebuildQueued = false;
                RebuildPreviewAndRefreshUiNow();
            });
        }

        private void RebuildPreviewAndRefreshUiNow()
        {
            var previousMode = mTargetMode;
            UpdateTargetMode();

            if (mFilterButtonsWrap != null && (mFilterButtonsMode != mTargetMode || previousMode != mTargetMode))
                RebuildActiveFilterButtons();

            mPreviewTargets.Clear();
            mPreviewNewNames.Clear();
            mPreviewRows.Clear();

            CollectTargets(mPreviewTargets);
            ComputeNewNames(mPreviewTargets, mPreviewNewNames);

            for (int i = 0; i < mPreviewTargets.Count; i++)
            {
                var row = new PreviewRow
                {
                    Target = mPreviewTargets[i].PreviewObject,
                    NewName = mPreviewNewNames[i]
                };
                mPreviewRows.Add(row);
            }

            if (mPreviewLabel != null)
                mPreviewLabel.text = $"Preview ({mPreviewTargets.Count})";

            if (mRenameButton != null)
            {
                var noun = mTargetMode == TargetMode.ProjectFiles ? "item(s)" : "object(s)";
                mRenameButton.text = $"Rename {mPreviewTargets.Count} {noun}";
                mRenameButton.SetEnabled(mPreviewTargets.Count > 0);
            }

            mPreviewList?.Rebuild();

            Repaint();
        }

        private void CollectTargets(List<RenameTarget> output)
        {
            if (mTargetMode == TargetMode.ProjectFiles)
            {
                CollectProjectTargets(output);
                return;
            }

            CollectSceneTargets(output);
        }

        private void CollectSceneTargets(List<RenameTarget> output)
        {
            var seen = new HashSet<int>();

            foreach (GameObject obj in Selection.gameObjects)
            {
                if (obj == null) continue;

                if (mRenameNested)
                {
                    CollectSceneRecursive(obj.transform, output, seen);
                }
                else if (seen.Add(obj.GetInstanceID()))
                {
                    if (PassesSceneFilter(obj))
                        output.Add(RenameTarget.FromSceneObject(obj));
                }
            }
        }

        private void CollectSceneRecursive(Transform t, List<RenameTarget> output, HashSet<int> seen)
        {
            if (t == null) return;
            var go = t.gameObject;

            if (seen.Add(go.GetInstanceID()))
            {
                if (PassesSceneFilter(go))
                    output.Add(RenameTarget.FromSceneObject(go));
            }

            for (int i = 0; i < t.childCount; i++)
                CollectSceneRecursive(t.GetChild(i), output, seen);
        }

        private void CollectProjectTargets(List<RenameTarget> output)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedPaths = GetSelectedProjectPaths();
            for (int i = 0; i < selectedPaths.Count; i++)
            {
                var path = selectedPaths[i];
                if (string.IsNullOrEmpty(path))
                    continue;

                CollectProjectPath(path, output, seen, mRenameNested);
            }
        }

        private void CollectProjectPath(string path, List<RenameTarget> output, HashSet<string> seen, bool includeChildren)
        {
            if (string.IsNullOrEmpty(path) || !seen.Add(path))
                return;

            var isFolder = AssetDatabase.IsValidFolder(path);
            if (PassesProjectFilter(path, isFolder))
                output.Add(RenameTarget.FromAssetPath(path, isFolder));

            if (!includeChildren || !isFolder)
                return;

            var childGuids = AssetDatabase.FindAssets(string.Empty, new[] { path });
            for (int i = 0; i < childGuids.Length; i++)
            {
                var childPath = AssetDatabase.GUIDToAssetPath(childGuids[i]);
                if (string.IsNullOrEmpty(childPath) || string.Equals(childPath, path, StringComparison.OrdinalIgnoreCase))
                    continue;

                var childIsFolder = AssetDatabase.IsValidFolder(childPath);
                if (!seen.Add(childPath))
                    continue;

                if (PassesProjectFilter(childPath, childIsFolder))
                    output.Add(RenameTarget.FromAssetPath(childPath, childIsFolder));
            }
        }

        private bool PassesSceneFilter(GameObject go)
        {
            if (!mFilterEnabled) return true;

            foreach (var filter in mTypeFilters)
            {
                if (!filter.Enabled) continue;
                if (filter.Type != null && go.GetComponent(filter.Type) != null) return true;
            }

            return false;
        }

        private bool PassesProjectFilter(string path, bool isFolder)
        {
            if (!mFilterEnabled) return true;

            for (int i = 0; i < mProjectFilters.Count; i++)
            {
                var filter = mProjectFilters[i];
                if (!filter.Enabled) continue;
                if (filter.Matches(path, isFolder)) return true;
            }

            return false;
        }

        #endregion

        #region Name Computation

        private void ComputeNewNames(List<RenameTarget> targets, List<string> outNames)
        {
            int counterAscending = mStartIndex;
            int counterDescending = mStartIndex;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var newName = ComputeName(target, counterAscending, counterDescending);
                outNames.Add(newName);
                counterAscending++;
                counterDescending--;
            }
        }

        private string ComputeName(RenameTarget target, int counterAscending, int counterDescending)
        {
            var original = target != null ? target.OriginalName : string.Empty;

            if (string.IsNullOrEmpty(mRename) && string.IsNullOrEmpty(mFind))
                return ApplyQuickCaseAction(original);

            var replacement = ExpandPattern(mRename, original, counterAscending, counterDescending, target);

            if (string.IsNullOrEmpty(mFind))
                return ApplyQuickCaseAction(replacement);

            var comparison = mCaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            var computed = ReplaceAll(original, mFind, replacement, comparison);
            return ApplyQuickCaseAction(computed);
        }

        private string ApplyQuickCaseAction(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            string transformed;

            switch (mQuickCaseAction)
            {
                case QuickCaseAction.Upper:
                    transformed = value.ToUpperInvariant();
                    break;
                case QuickCaseAction.Lower:
                    transformed = value.ToLowerInvariant();
                    break;
                case QuickCaseAction.Camel:
                    transformed = ToCamelCase(value);
                    break;
                case QuickCaseAction.InverseCamel:
                    transformed = ToInverseCamelCase(value);
                    break;
                default:
                    transformed = value;
                    break;
            }

            return transformed;
        }

        private static string ToCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var tokens = CamelCaseWordRegex.Matches(value);
            if (tokens.Count == 0)
                return value;

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < tokens.Count; i++)
            {
                var word = tokens[i].Value;
                if (string.IsNullOrEmpty(word)) continue;

                var lower = word.ToLowerInvariant();
                if (i == 0)
                {
                    sb.Append(lower);
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(lower[0]));
                    if (lower.Length > 1)
                        sb.Append(lower.Substring(1));
                }
            }

            return sb.ToString();
        }

        private static string ToInverseCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            StringBuilder sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (char.IsUpper(c))
                {
                    bool prevIsUpper = i > 0 && char.IsUpper(value[i - 1]);
                    bool prevIsLowerOrDigit = i > 0 && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]));
                    bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);

                    bool isAcronymStart = i == 0 && i + 1 < value.Length && char.IsUpper(value[i + 1]);
                    bool isWordBoundaryFromAcronym = prevIsUpper && nextIsLower;

                    if (i > 0 && (prevIsLowerOrDigit || isWordBoundaryFromAcronym) && value[i - 1] != '_')
                        sb.Append('_');
                    else if (isAcronymStart)
                        sb.Append('_');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private string ExpandPattern(string pattern, string originalName, int counterAscending, int counterDescending, RenameTarget target)
        {
            if (string.IsNullOrEmpty(pattern)) return string.Empty;

            string withCounter = IncrementerRegex.Replace(pattern, match =>
            {
                string tokenBody = match.Groups[1].Value;
                int digits = tokenBody.Length;
                bool isUpper = tokenBody[0] == 'N';
                int counter = isUpper ? counterDescending : counterAscending;
                return counter.ToString("D" + digits);
            });

            string withOriginal = withCounter.Replace(MarkerOriginal, originalName);
            string withTypes = withOriginal.Replace("$TT", BuildTypeListToken(target));

            string withConditional = ConditionalTypeRegex.Replace(withTypes, match =>
            {
                string requestedType = match.Groups[1].Value;
                return ResolveConditionalTypeToken(target, requestedType);
            });

            return withConditional;
        }

        private static void TuneInputField(VisualElement field)
        {
            if (field == null) return;

            field.style.minHeight = InputHeight;
            field.style.unityFontStyleAndWeight = FontStyle.Normal;

            VisualElement inputElement = field.Q(className: "unity-base-text-field__input") ??
                                        field.Q(className: "unity-text-input");
            if (inputElement == null) return;

            inputElement.style.fontSize = InputFontSize;
            inputElement.style.minHeight = InputHeight;
            inputElement.style.paddingTop = 2;
            inputElement.style.paddingBottom = 2;
        }

        private string BuildTypeListToken(RenameTarget target)
        {
            if (target == null) return string.Empty;

            if (target.IsProjectAsset)
                return BuildProjectTypeListToken(target.AssetPath, target.IsFolder);

            GameObject go = target.SceneObject;
            if (go == null) return string.Empty;

            StringBuilder sb = new StringBuilder(48);
            foreach (TypeFilterEntry entry in mTypeFilters)
            {
                if (entry.Type == null) continue;
                if (go.GetComponent(entry.Type) != null)
                {
                    if (sb.Length > 0)
                        sb.Append('_');
                    sb.Append(entry.Token);
                }
            }

            return sb.ToString();
        }

        private string BuildProjectTypeListToken(string path, bool isFolder)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < mProjectFilters.Count; i++)
            {
                ProjectFilterEntry entry = mProjectFilters[i];
                if (entry.Matches(path, isFolder))
                {
                    if (sb.Length > 0)
                        sb.Append('_');
                    sb.Append(entry.Token);
                }
            }

            return sb.ToString();
        }

        private string ResolveConditionalTypeToken(RenameTarget target, string requestedType)
        {
            if (target == null || string.IsNullOrEmpty(requestedType)) return string.Empty;

            if (target.IsProjectAsset)
                return ResolveProjectConditionalTypeToken(target.AssetPath, target.IsFolder, requestedType);

            GameObject go = target.SceneObject;
            if (go == null) return string.Empty;

            string key = NormalizeTokenKey(requestedType);
            foreach (TypeFilterEntry entry in mTypeFilters)
            {
                if (!TypeTokenMatches(entry, key))
                    continue;

                return entry.Type != null && go.GetComponent(entry.Type) != null
                    ? entry.Token
                    : string.Empty;
            }

            return string.Empty;
        }

        private string ResolveProjectConditionalTypeToken(string path, bool isFolder, string requestedType)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(requestedType))
                return string.Empty;

            string key = NormalizeTokenKey(requestedType);
            for (int i = 0; i < mProjectFilters.Count; i++)
            {
                ProjectFilterEntry entry = mProjectFilters[i];
                if (entry.NormalizedToken != key && entry.NormalizedLabel != key)
                    continue;

                return entry.Matches(path, isFolder) ? entry.Token : string.Empty;
            }

            return string.Empty;
        }

        private static bool TypeTokenMatches(TypeFilterEntry entry, string key)
        {
            if (entry == null || string.IsNullOrEmpty(key)) return false;

            if (entry.NormalizedToken == key) return true;
            if (entry.NormalizedLabel == key) return true;
            if (entry.NormalizedTypeName == key) return true;

            return false;
        }

        private static string NormalizeTokenKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private static string ReplaceAll(string source, string find, string replacement, StringComparison cmp)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(find)) return source;

            StringBuilder sb = new StringBuilder(source.Length);
            int idx = 0;

            while (idx < source.Length)
            {
                int hit = source.IndexOf(find, idx, cmp);
                if (hit < 0)
                {
                    sb.Append(source, idx, source.Length - idx);
                    break;
                }

                sb.Append(source, idx, hit - idx);
                sb.Append(replacement);
                idx = hit + find.Length;
            }

            return sb.ToString();
        }

        #endregion

        #region Apply Rename

        private void ApplyRename()
        {
            var targets = new List<RenameTarget>();
            var newNames = new List<string>();
            BuildRenameBatch(targets, newNames);

            if (targets.Count == 0) return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Batch Rename");

            int changed = mTargetMode == TargetMode.ProjectFiles
                ? ApplyProjectRenames(targets, newNames)
                : ApplySceneRenames(targets, newNames);

            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"[BatchRenamer] Переименовано {changed} из {targets.Count} элементов.");
            RebuildPreviewAndRefreshUiNow();
        }

        private void BuildRenameBatch(List<RenameTarget> targets, List<string> newNames)
        {
            CollectTargets(targets);
            ComputeNewNames(targets, newNames);
        }

        private int ApplyProjectRenames(List<RenameTarget> targets, List<string> newNames)
        {
            int changed = 0;
            var order = BuildProjectRenameOrder(targets);

            for (int j = 0; j < order.Count; j++)
            {
                int i = order[j];
                var target = targets[i];
                if (target == null || string.IsNullOrEmpty(target.AssetPath))
                    continue;

                var newName = newNames[i];
                if (string.IsNullOrEmpty(newName) || string.Equals(newName, target.OriginalName, StringComparison.Ordinal))
                    continue;

                var error = AssetDatabase.RenameAsset(target.AssetPath, newName);
                if (string.IsNullOrEmpty(error))
                {
                    changed++;
                }
                else
                {
                    Debug.LogWarning("[BatchRenamer] Не удалось переименовать asset: " + target.AssetPath + " -> " + newName + " | " + error);
                }
            }

            if (changed > 0)
                AssetDatabase.SaveAssets();

            return changed;
        }

        private static List<int> BuildProjectRenameOrder(List<RenameTarget> targets)
        {
            var order = new List<int>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
                order.Add(i);

            order.Sort((a, b) =>
            {
                int da = GetPathDepth(targets[a].AssetPath);
                int db = GetPathDepth(targets[b].AssetPath);
                return db.CompareTo(da);
            });

            return order;
        }

        private static int ApplySceneRenames(List<RenameTarget> targets, List<string> newNames)
        {
            int changed = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                var go = targets[i].SceneObject;
                if (go == null) continue;

                var newName = newNames[i];
                if (string.IsNullOrEmpty(newName) || newName == go.name) continue;

                Undo.RecordObject(go, "Batch Rename");
                go.name = newName;
                EditorUtility.SetDirty(go);
                changed++;
            }

            return changed;
        }

        private static int GetPathDepth(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int depth = 1;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '/')
                    depth++;
            }

            return depth;
        }

        #endregion

        #region Data Models

        [Serializable]
        private abstract class FilterEntryBase
        {
            public string Label;
            public bool Enabled;

            [NonSerialized]
            public Button UiButton;

            protected FilterEntryBase(string label)
            {
                Label = label;
                Enabled = true;
            }
        }

        [Serializable]
        private class TypeFilterEntry : FilterEntryBase
        {
            public Type Type;

            public string Token;

            public string NormalizedToken;

            public string NormalizedLabel;

            public string NormalizedTypeName;

            public TypeFilterEntry(string label, Type type, string token)
                : base(label)
            {
                Type = type;
                Token = token;
                NormalizedToken = NormalizeTokenKey(token);
                NormalizedLabel = NormalizeTokenKey(label);
                NormalizedTypeName = type != null ? NormalizeTokenKey(type.Name) : string.Empty;
            }
        }

        [Serializable]
        private class ProjectFilterEntry : FilterEntryBase
        {
            public string Token;
            public bool FolderOnly;
            public string[] Extensions;

            public string NormalizedToken;

            public string NormalizedLabel;

            public ProjectFilterEntry(string label, string token, params string[] extensions)
                : this(label, token, false, extensions)
            {
            }

            public ProjectFilterEntry(string label, string token, bool folderOnly, params string[] extensions)
                : base(label)
            {
                Token = token;
                FolderOnly = folderOnly;
                Extensions = extensions ?? Array.Empty<string>();
                NormalizedToken = NormalizeTokenKey(token);
                NormalizedLabel = NormalizeTokenKey(label);
            }

            public bool Matches(string assetPath, bool isFolder)
            {
                if (FolderOnly)
                    return isFolder;

                if (isFolder)
                    return false;

                string extension = System.IO.Path.GetExtension(assetPath);
                if (string.IsNullOrEmpty(extension))
                    return false;

                for (int i = 0; i < Extensions.Length; i++)
                {
                    if (string.Equals(Extensions[i], extension, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
        }

        private class RenameTarget
        {
            public GameObject SceneObject;
            public string AssetPath;
            public bool IsFolder;
            public string OriginalName;
            public UnityEngine.Object PreviewObject;

            public bool IsProjectAsset => !string.IsNullOrEmpty(AssetPath);

            public static RenameTarget FromSceneObject(GameObject go)
            {
                return new RenameTarget
                {
                    SceneObject = go,
                    OriginalName = go != null ? go.name : string.Empty,
                    PreviewObject = go
                };
            }

            public static RenameTarget FromAssetPath(string path, bool isFolder)
            {
                string name = isFolder
                    ? new System.IO.DirectoryInfo(path).Name
                    : System.IO.Path.GetFileNameWithoutExtension(path);

                UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(path);

                return new RenameTarget
                {
                    AssetPath = path,
                    IsFolder = isFolder,
                    OriginalName = name,
                    PreviewObject = obj
                };
            }
        }

        private class PreviewRow
        {
            public UnityEngine.Object Target;
            public string NewName;
        }

        #endregion
    }
}
