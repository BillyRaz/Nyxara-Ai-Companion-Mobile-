// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using Nyxara.AICompanion.Data;
using UnityEngine;

namespace Nyxara.AICompanion.Runtime
{
    public class CompanionActionExecutor : MonoBehaviour
    {
        [SerializeField] private Transform companionTransform;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private float followDistance = 2f;
        [SerializeField] private float followSpeed = 3f;

        private string _currentAction = "none";

        public void Execute(string action, NPCRuntimeState state)
        {
            _currentAction = action;

            switch (action.ToLowerInvariant())
            {
                case "follow":
                    StartFollowing();
                    break;
                case "wait":
                    StopMoving();
                    if (state != null) state.currentTask = "waiting";
                    break;
                case "stop":
                    StopMoving();
                    if (state != null) state.currentTask = "idle";
                    break;
                case "focus_player":
                    FocusOnPlayer();
                    break;
                case "warn":
                    PlayWarning();
                    break;
            }
        }

        private void StartFollowing()
        {
            Debug.Log("Action: Starting to follow player");
        }

        private void StopMoving()
        {
            Debug.Log("Action: Stopping movement");
            _currentAction = "none";
        }

        private void FocusOnPlayer()
        {
            if (companionTransform == null || playerTransform == null)
            {
                return;
            }

            var direction = playerTransform.position - companionTransform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                companionTransform.rotation = Quaternion.LookRotation(direction.normalized);
            }
        }

        private void PlayWarning()
        {
            Debug.Log("Action: Playing warning animation/sound");
        }

        private void Update()
        {
            if (_currentAction == "follow" && companionTransform != null && playerTransform != null)
            {
                var targetPos = playerTransform.position - (playerTransform.forward * followDistance);
                companionTransform.position = Vector3.MoveTowards(companionTransform.position, targetPos, followSpeed * Time.deltaTime);
                FocusOnPlayer();
            }
        }
    }
}
