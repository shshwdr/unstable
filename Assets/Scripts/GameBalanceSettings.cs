using UnityEngine;

[CreateAssetMenu(fileName = "GameBalanceSettings", menuName = "Balance/Game Balance Settings")]
public class GameBalanceSettings : ScriptableObject
{
    [Header("物理")]
    public float gravity = -9.81f;
    [Range(0f, 2f)] public float friction = 0.55f;
    [Range(0f, 1f)] public float bounciness = 0f;
    public float buildingDensity = 1.4f;

    [Header("平衡板")]
    public float boardWidth = 11f;
    public float boardHeight = 0.35f;
    public float boardMass = 8f;
    public float boardAngularDrag = 2f;
    public Vector2 pivotPosition = new Vector2(0f, -2.5f);

    [Header("回正")]
    [Tooltip("倾角小于这个值时才回正，超过后完全靠建筑配平。")]
    public float restoreAngle = 12f;
    [Tooltip("把板拉回水平的弹簧强度。0 表示完全靠建筑左右配平。")]
    public float restoreStrength = 18f;
    [Tooltip("回正时的阻尼，越大越不容易来回晃。")]
    public float restoreDamping = 2.5f;

    [Header("放置")]
    [Tooltip("放下时把重叠建筑再挤开的额外距离。")]
    public float overlapPush = 0.12f;

    [Header("失败判定")]
    public bool failOnTilt = true;
    public float failAngle = 30f;
    public float failHoldSeconds = 1.2f;
}
