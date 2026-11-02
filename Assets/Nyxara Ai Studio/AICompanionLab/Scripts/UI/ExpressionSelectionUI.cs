// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System.Collections.Generic;
using System.Linq;
using Nyxara.AICompanion.Expressions;
using UnityEngine;
using UnityEngine.UI;

namespace Nyxara.AICompanion.UI
{
    public class ExpressionSelectionUI : MonoBehaviour
    {
        [SerializeField] private Transform buttonContainer;
        [SerializeField] private GameObject buttonPrefab;
        [SerializeField] private ExpressionLibraryManager libraryManager;
        [SerializeField] private ExpressionCategory categoryFilter = ExpressionCategory.Emotion;

        private readonly List<Button> _buttons = new();

        private void Start()
        {
            if (libraryManager == null)
            {
                libraryManager = FindFirstObjectByType<ExpressionLibraryManager>();
            }

            if (libraryManager != null)
            {
                libraryManager.OnLibraryUpdated += RefreshUI;
                RefreshUI(libraryManager.LoadedPresets);
            }
        }

        private void RefreshUI(IReadOnlyList<ExpressionPreset> presets)
        {
            foreach (var btn in _buttons)
            {
                if (btn != null)
                {
                    Destroy(btn.gameObject);
                }
            }

            _buttons.Clear();
            var filtered = presets.Where(p => p.category == categoryFilter).ToList();
            foreach (var preset in filtered)
            {
                var btnObj = Instantiate(buttonPrefab, buttonContainer);
                var btn = btnObj.GetComponent<Button>();
                var text = btnObj.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = preset.displayName;
                }

                var capturedPreset = preset;
                btn.onClick.AddListener(() => libraryManager.ApplyPreset(capturedPreset));
                _buttons.Add(btn);
            }
        }

        private void OnDestroy()
        {
            if (libraryManager != null)
            {
                libraryManager.OnLibraryUpdated -= RefreshUI;
            }
        }
    }
}
