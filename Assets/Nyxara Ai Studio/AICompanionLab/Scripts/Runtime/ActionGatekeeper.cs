// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using Nyxara.AICompanion.Data;
using UnityEngine;

namespace Nyxara.AICompanion.Runtime
{
    public class ActionGatekeeper : MonoBehaviour
    {
        [SerializeField] private CompanionActionExecutor actionExecutor;

        private readonly HashSet<string> _allowedActions = new()
        {
            "none", "follow", "wait", "stop", "focus_player", "warn"
        };

        private readonly Dictionary<string, Func<NPCRuntimeState, bool>> _actionConditions = new()
        {
            ["follow"] = state => state != null && !state.followState,
            ["wait"] = state => state != null && state.currentTask != "waiting",
            ["stop"] = state => state != null && (state.followState || state.currentTask == "following"),
            ["focus_player"] = state => true,
            ["warn"] = state => state != null && state.dangerLevel > 0.3f
        };

        public bool ValidateAction(string action, NPCRuntimeState state)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            var normalizedAction = action.ToLowerInvariant();
            if (!_allowedActions.Contains(normalizedAction))
            {
                Debug.LogWarning($"Action '{normalizedAction}' is not in allowed actions list.");
                return false;
            }

            if (normalizedAction == "none")
            {
                return true;
            }

            if (_actionConditions.TryGetValue(normalizedAction, out var condition))
            {
                var isValid = condition(state);
                if (!isValid)
                {
                    Debug.Log($"Action '{normalizedAction}' rejected by condition.");
                }

                return isValid;
            }

            return true;
        }

        public bool TryExecuteAction(string action, NPCRuntimeState state)
        {
            if (!ValidateAction(action, state))
            {
                return false;
            }

            if (actionExecutor != null && !string.Equals(action, "none", StringComparison.OrdinalIgnoreCase))
            {
                actionExecutor.Execute(action, state);
            }

            if (string.Equals(action, "follow", StringComparison.OrdinalIgnoreCase) && state != null)
            {
                state.followState = true;
                state.currentTask = "following";
            }
            else if (string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase) && state != null)
            {
                state.followState = false;
            }

            return true;
        }
    }
}
