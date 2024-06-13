using Com.IsartDigital.Common.Audio;
using Com.IsartDigital.Scepulturia.Inputs;
using Com.IsartDigital.Scepulturia.Level;
using FMODUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Com.IsartDigital.Scepulturia.Choice
{
    public delegate void PlayerChoiceEventHandler();

    public class PlayerChoice : MonoBehaviour
    {
        [Header("Control parameters")]
        [SerializeField, Range(0.01f, 0.5f)] private float screenWidthRatioAcceptanceZoneHold = 0.2f;
        [SerializeField, Range(0.01f, 0.5f)] private float screenWidthRatioAcceptanceZoneUp = 0.2f;
        [Space(5)]
        [SerializeField, Range(5f, 20f)] private float endAngle = 10f;
        [Space(5)]
        [SerializeField] private float timeToDrag = 0.2f;
        [SerializeField] private float distanceToDrag = 30;

        [Space(10)]
        [Header("Minimum swipe parameters")]
        [SerializeField, Range(0.01f, 0.5f)] private float minimumSwipeDistanceMinScreenRatio = 0.05f;
        [SerializeField, Range(0.01f, 0.5f)] private float minimumSwipeDistanceMaxScreenRatio = 0.5f;
        [Space(5)]
        [SerializeField] private float minimumSwipeDistanceTimerMax = 1f;

        [Space(10)]
        [Header("Control limit parameters")]
        [SerializeField] private float yMovementInfluence = 0.2f;
        [SerializeField] private float xMovementInfluence = 0.2f;

        [Space(10)]
        [Header("Feedback parameters")]
        [SerializeField, Range(0.01f, 0.05f)] private float screenWidthRatioVibrationZone = 0.01f;
        [SerializeField] private long vibrationDurationInMilisecond = 500L;

        [Space(10)]
        [Header("SFX")]
        [SerializeField] private EventReference swipeUnauthorizedSFX = default;

        [Space(10)]
        [Header("FTUE")]
        [SerializeField] private float ftueCooldown = 3f;

        private Card currentCard;
        private Card lastCard;

        private CardGenerator cardGenerator;
        private ChoiceManager choiceManager;

        private Vector2 dragStartPosition;
        private Vector2 dragDistance;
        private Vector2 downPointPosition;

        private Vector3 cardOrigin;
        private Vector3 dragDirection;
        private Vector3 dragDirectionNormalized;
        private Vector3 dragStartDistance;

        private bool canUseVibration = true;
        private bool canSetUnfoldButton = false;

        private Coroutine resetCoroutine;

        private float minimumSwipeDistanceElapsedTime = 0f;
        private float screenWidth;
        private float screenHeight;

        private Quaternion initialRotation;
        private Coroutine cooldown;

        private float downTimeStamp;
        private bool inDrag;
        private Vector3 touchPosition;

        private void Start()
        {
            cardGenerator = CardGenerator.Instance;

            choiceManager = ChoiceManager.Instance;
            choiceManager.OnChoicePhaseStarted += ChoiceManager_OnChoicePhaseStarted;
            choiceManager.OnChoiceMatch += ChoiceManager_OnChoicePhaseEnd;
            choiceManager.OnCardSpawned += ChoiceManager_OnCardSpawned;

            cardOrigin = cardGenerator.CardOrigin;

            screenWidth = Screen.width;
            screenHeight = Screen.height;
        }


        private void ChoiceManager_OnCardSpawned()
        {
            initialRotation = Quaternion.identity;//choiceManager.CurrentCard.transform.rotation;
        }

        private void OnDestroy()
        {
            if (choiceManager)
            {
                choiceManager.OnChoicePhaseStarted -= ChoiceManager_OnChoicePhaseStarted;
                choiceManager.OnChoiceMatch -= ChoiceManager_OnChoicePhaseEnd;
                choiceManager.OnCardSpawned -= ChoiceManager_OnCardSpawned;
            }

            if (TouchManager.Instance)
            {
                //Events unsubscribes
                TouchManager.Instance.OnUp -= TouchManager_OnUp;
                TouchManager.Instance.OnDown -= TouchManager_OnDown;
                TouchManager.Instance.OnHold -= TouchManager_OnHold;
            }
        }

        private void Update()
        {
            if (!inDrag)
            {
                if (downTimeStamp > timeToDrag
                    || ((downPointPosition - (Vector2)touchPosition).sqrMagnitude > distanceToDrag * distanceToDrag))
                {
                    StartDrag(downPointPosition);
                }
            }

            if (currentCard != null)
            {
                //Rotate card
                float endAngleCalculation;
                if (currentCard.transform.localPosition.x > 0)
                {
                    endAngleCalculation = downPointPosition.y < screenHeight / 2 ? endAngle : -1 * endAngle;
                }
                else
                {
                    endAngleCalculation = downPointPosition.y < screenHeight / 2 ? -1 * endAngle : endAngle;
                }

                Quaternion targetRotation = Quaternion.AngleAxis(endAngleCalculation, Vector3.forward);

                currentCard.transform.rotation = Quaternion.SlerpUnclamped(initialRotation, targetRotation, Mathf.Abs(currentCard.transform.localPosition.x / (screenWidth / 2)));
            }
        }

        private void ChoiceManager_OnChoicePhaseStarted()
        {
            //Events Subscriptions
            TouchManager.Instance.OnUp += TouchManager_OnUp;
            TouchManager.Instance.OnDown += TouchManager_OnDown;
            TouchManager.Instance.OnHold += TouchManager_OnHold;
        }
        private void ChoiceManager_OnChoicePhaseEnd()
        {
            TouchManager.Instance.OnUp -= TouchManager_OnUp;
            TouchManager.Instance.OnDown -= TouchManager_OnDown;
            TouchManager.Instance.OnHold -= TouchManager_OnHold;
            gameObject.SetActive(false);
        }

        private void TouchManager_OnDown(Vector2 point)
        {
            touchPosition = TouchManager.Instance.Position;
            downPointPosition = point;
        }

        private void StartDrag(Vector2 point)
        {
            //Set currentCard var with property contained in cardGenerator
            if (choiceManager.CurrentCard != null && RectTransformUtility.RectangleContainsScreenPoint(choiceManager.CurrentCard.ClickableZone, point) && !choiceManager.BlockSwipe && !choiceManager.IsInPause)
            {
                if (resetCoroutine != null)
                {
                    StopCoroutine(resetCoroutine);
                }

                if (cooldown == null)
                {
                    choiceManager.DesactivateFTUE();
                }

                minimumSwipeDistanceElapsedTime = 0f;
                inDrag = true;

                //Set dragStartPosition with the touch position
                dragStartPosition = point;

                //Distance between cardOrigin and the input
                dragStartDistance = cardOrigin - (Vector3)dragStartPosition;

                currentCard = choiceManager.CurrentCard;
                currentCard.Down();
            }
        }

        private void TouchManager_OnHold(Vector2 point)
        {
            touchPosition = TouchManager.Instance.Position;
            downTimeStamp += Time.deltaTime;

            //Check if a card exist and if a card has been selected in TouchManager_OnDown
            if (inDrag && currentCard != null)
            {
                minimumSwipeDistanceElapsedTime += Time.deltaTime;

                if (minimumSwipeDistanceElapsedTime > timeToDrag)
                {
                    canSetUnfoldButton = true;
                    currentCard.UnfoldButton.interactable = false;
                }

                SetDragDirection(currentCard);

                currentCard.transform.position = GetCardPosition(point);
            }
        }

        public void TouchManager_OnUp(Vector2 point)
        {
            touchPosition = Vector3.zero;
            downPointPosition = Vector3.zero;
            downTimeStamp = 0;

            if ((inDrag && currentCard != null && currentCard.HasBeenSelected && currentCard != lastCard))
            {
                inDrag = false;

                if (cooldown == null && !choiceManager.BlockSwipe)
                {
                    cooldown = StartCoroutine(FTUECooldown());
                }

                currentCard.Up();

                if (canSetUnfoldButton)
                {
                    canSetUnfoldButton = false;
                    currentCard.UnfoldButton.interactable = true;
                }

                SetDragDirection(currentCard);

                //(Check if timer is inferior + Check if the position of the card is under the minimumSwipeDistanceMinScreenRatio) OR Check if the position of the card is superior  the screenWidthRatioAcceptanceZoneUp

                if (minimumSwipeDistanceElapsedTime != 0 && minimumSwipeDistanceElapsedTime <= minimumSwipeDistanceTimerMax
                    && ((!choiceManager.HasExceedNoMaxNumber && currentCard.transform.localPosition.x < -minimumSwipeDistanceMinScreenRatio * screenWidth / 2 && !choiceManager.DislikeIsLocked) || (!currentCard.UnitDataLinked.IsLocked && currentCard.transform.localPosition.x > minimumSwipeDistanceMinScreenRatio * screenWidth / 2 && !choiceManager.LikeIsLocked))
                    || GetDragDistanceMoved() >= screenWidthRatioAcceptanceZoneUp * screenWidth)
                {
                    AllowDrag();
                    ChoiceConfirmed(currentCard, point);
                    return;
                }

                resetCoroutine = StartCoroutine(choiceManager.ResetToOrigin(currentCard));
            }
        }

        public void ForceUp()
        {
            touchPosition = Vector3.zero;
            downPointPosition = Vector3.zero;
            downTimeStamp = 0;

            inDrag = false;

            if (cooldown == null && !choiceManager.BlockSwipe)
            {
                cooldown = StartCoroutine(FTUECooldown());
            }

            choiceManager.CurrentCard.Up();

            if (canSetUnfoldButton)
            {
                canSetUnfoldButton = false;
                choiceManager.CurrentCard.UnfoldButton.interactable = true;
            }

            SetDragDirection(choiceManager.CurrentCard);
        }

        public void AllowDrag()
        {
            inDrag = false;
        }

        private Vector3 GetCardPosition(Vector2 point)
        {
            //Point = touch position
            dragDistance = point - dragStartPosition;

            Vector3 cardPosition;

            //Lerp to restrain the player movement 
            float yPositionInfluenced = Mathf.Lerp(dragStartPosition.y + dragStartDistance.y, cardOrigin.y + dragDistance.y, yMovementInfluence);
            float xPositionInfluenced = Mathf.Lerp(dragStartPosition.x + dragStartDistance.x, cardOrigin.x + dragDistance.x, xMovementInfluence);

            //If the player has discarded OR accepted too many card, he cant move to left (discard side) OR to the right (accept side)
            if ((currentCard.UnitDataLinked.IsLocked
                || choiceManager.LikeIsLocked
                || choiceManager.HasExceedYesMaxNumber)
                && currentCard.transform.localPosition.x > 0
                || (choiceManager.HasExceedNoMaxNumber
                || choiceManager.DislikeIsLocked)
                && currentCard.transform.localPosition.x < 0)
            {
                PlayFeedbacks();
                cardPosition = new Vector3(xPositionInfluenced,
                   yPositionInfluenced,
                    cardOrigin.z);
            }
            else
            {
                cardPosition = new Vector3(cardOrigin.x + dragDistance.x,
                  yPositionInfluenced,
                    cardOrigin.z);
            }

            return cardPosition;
        }

        private void PlayFeedbacks()
        {
            if (canUseVibration && Mathf.Abs(currentCard.transform.localPosition.x) >= screenWidthRatioVibrationZone * (screenWidth / 2))
            {
                Audio.Play(swipeUnauthorizedSFX);
                Vibration.Vibrate(vibrationDurationInMilisecond);
                canUseVibration = false;
            }
            else if (!canUseVibration && (Mathf.Abs(currentCard.transform.localPosition.x) < screenWidthRatioVibrationZone * (screenWidth / 2) || !currentCard.HasBeenSelected))
            {
                canUseVibration = true;
            }

        }

        private void ChoiceConfirmed(Card card, Vector2 touchPos)
        {
            choiceManager.LastTouchPos = touchPos;
            //if card.transform.localPosition.x > 0 => Card refused
            //if card.transform.localPosition.x < 0 => Card accepted
            choiceManager.ActionHasBeenMade(card, card.transform.localPosition.x > 0);
            lastCard = card;
        }

        private void SetDragDirection(Card currentCard)
        {
            dragDirection = currentCard.transform.localPosition;
            dragDirectionNormalized = Vector3.Normalize(dragDirection);
        }

        private float GetDragDistanceMoved()
        {
            float distanceMoved = Math.Abs(dragDirection.x);
            return distanceMoved;
        }

        private IEnumerator FTUECooldown()
        {
            yield return new WaitForSeconds(ftueCooldown);

            if (choiceManager.CurrentCard && !choiceManager.CurrentCard.HasBeenSelected)
            {
                choiceManager.ActivateFTUE();
            }

            cooldown = null;
        }

    }
}
