using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ThumbstickUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler, IDragHandler {
    public ThumbstickUISettings settings;
    public new Camera camera;
    public RectTransform rectTransform {get {return (RectTransform)transform;}}
    public CanvasGroup canvasGroup;

    [Space]
    // public RectTransform _container;
    // public RectTransform container {
    //     get {
    //         return _container == null ? rectTransform : _container;
    //     }
    // }
    public RectTransform thumbstickRectTransform;
    public RectTransform dragAreaRectTransform;
    public RectTransform background;
    public RectTransform thumbstickActiveRectTransform;
    public CanvasGroup thumbstickCanvasGroup;
    
    [Space]
    // Master showing toggle, for show/hiding externally
    public bool allowShowing = true;
    public bool hideWhenNotHeld = false;
    public bool showing => allowShowing && (!hideWhenNotHeld || held);
    
    [Space]
    public bool moveThumbstickOnDown = false;
    
    [Space]
    [System.NonSerialized]
    public bool held;
    [System.NonSerialized]
    public int currentPointerID = -1;
    [System.NonSerialized]
    public Vector2 movementVector;
    
    Vector2 clampedLocalDragPos;
    Vector2 snapbackVelocity;
    
    float heldScale;
    float heldScaleVelocity;

    float targetThumbstickAngle;
    float thumbstickAngle;
    float thumbstickAngleVelocity;
    float targetThumbstickMagnitude;
    float thumbstickMagnitude;
    float thumbstickMagnitudeVelocity;

    void OnEnable () {
        held = false;
        movementVector = Vector2.zero;
        clampedLocalDragPos = Vector2.zero;
        snapbackVelocity = Vector2.zero;
        heldScale = settings.unheldScale;
        thumbstickCanvasGroup.alpha = 0;
        canvasGroup.alpha = 0;
    }
    void OnDisable () {}
    
    void RefreshDrag (Vector2 screenPosition) {
        Vector2 localDragPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(dragAreaRectTransform, screenPosition, camera, out localDragPos);   
        clampedLocalDragPos = Vector2.ClampMagnitude(localDragPos, Mathf.Min(dragAreaRectTransform.rect.width*0.5f, dragAreaRectTransform.rect.height*0.5f));
        movementVector = new Vector2(clampedLocalDragPos.x/dragAreaRectTransform.rect.width, clampedLocalDragPos.y/dragAreaRectTransform.rect.height) * 2;
        targetThumbstickAngle = Vector2.SignedAngle(Vector2.up, movementVector);
        targetThumbstickMagnitude = movementVector.magnitude;
    }

    void Update() {
        if(!allowShowing) {
            OnEndDrag(null);
        }
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, showing ? 1 : 0, settings.showingAlphaFadeSpeed * Time.deltaTime);

        if(held) {
            thumbstickCanvasGroup.alpha = Mathf.MoveTowards(thumbstickCanvasGroup.alpha, targetThumbstickMagnitude < settings.thumbstickShowMagnitude ? 0 : 1, settings.thumbstickAlphaFadeInSpeed * Time.deltaTime);
        } else {
            movementVector = Vector2.zero;
            clampedLocalDragPos = Vector2.SmoothDamp(clampedLocalDragPos, Vector2.zero, ref snapbackVelocity, settings.snapbackSmoothTime, settings.snapbackMaxSpeed, Time.deltaTime);
            thumbstickCanvasGroup.alpha = Mathf.MoveTowards(thumbstickCanvasGroup.alpha, 0, settings.thumbstickAlphaFadeOutSpeed * Time.deltaTime);
            targetThumbstickMagnitude = 0;
        }
        thumbstickMagnitude = Mathf.SmoothDamp(thumbstickMagnitude, targetThumbstickMagnitude, ref thumbstickMagnitudeVelocity, settings.thumbstickMagnitudeSmoothTime);
        thumbstickAngle = Mathf.SmoothDampAngle(thumbstickAngle, targetThumbstickAngle, ref thumbstickAngleVelocity, settings.thumbstickAngleSmoothTime);

        heldScale = SpringDamper.CriticallyDampedSpring(heldScale, held ? 1 : settings.unheldScale, ref heldScaleVelocity, settings.heldScaleSpringConstant);
        background.localScale = Vector3.one * heldScale;
        if(settings.positionWithPivot) {
            thumbstickActiveRectTransform.pivot = new Vector2(0.5f, Mathf.Lerp(settings.minPivot, settings.maxPivot, thumbstickMagnitude));
            thumbstickActiveRectTransform.localEulerAngles = new Vector3(0,0,thumbstickAngle);
            thumbstickActiveRectTransform.localPosition = Vector2.zero;
        } else {
            var thumbstickRads = (thumbstickAngle+90) * Mathf.Deg2Rad;
            thumbstickActiveRectTransform.localPosition = new Vector3(
                Mathf.Cos(thumbstickRads) * thumbstickMagnitude * dragAreaRectTransform.rect.width * 0.5f, 
                Mathf.Sin(thumbstickRads) * thumbstickMagnitude * dragAreaRectTransform.rect.height * 0.5f, 
                0
            );
            thumbstickActiveRectTransform.localEulerAngles = new Vector3(0,0,thumbstickAngle);
        }
    }
    
    public void OnPointerDown(PointerEventData eventData) {
        if (eventData.pointerId < -1) return;
        if(moveThumbstickOnDown) {
            Vector2 localDragPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)thumbstickRectTransform.parent, eventData.position, camera, out localDragPos);  
            thumbstickRectTransform.localPosition = localDragPos;
        }
        OnBeginDrag(eventData);
    }
    public void OnPointerUp(PointerEventData eventData) {
        if (eventData.pointerId < -1) return;
        OnEndDrag(eventData);
    }
    public void OnBeginDrag(PointerEventData eventData) {
        if (eventData.pointerId < -1) return;
        if(!allowShowing) return;
        currentPointerID = eventData.pointerId;
        held = true;
        thumbstickAngle = targetThumbstickAngle;
    }

    public void OnDrag(PointerEventData eventData) {
        if (eventData.pointerId < -1) return;
        if(!allowShowing) return;
        RefreshDrag(eventData.position);
    }
    public void OnEndDrag(PointerEventData eventData) {
        if (eventData.pointerId < -1) return;
        held = false;
        currentPointerID = -1;
    }
}