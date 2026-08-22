using UnityEngine;

[CreateAssetMenu(fileName = "GameBalanceSettings", menuName = "Balance/Game Balance Settings")]
public class GameBalanceSettings : ScriptableObject
{
    [Header("物理")]
    public float gravity = -9.81f;
    [Range(0f, 2f)] public float friction = 0.55f;
    [Range(0f, 1f)] public float bounciness = 0f;
    public float buildingDensity = 1.4f;
    [Tooltip("建筑刚体的角阻尼。越大越不容易持续旋转，Unity 默认约 0.05。")]
    public float buildingAngularDrag = 2f;

    [Header("平衡板")]
    public float boardHeight = 0.35f;
    public float boardMass = 8f;
    public float boardAngularDrag = 2f;
    [Tooltip("支点梯形短边相对底边的宽度比例。1 为与底边等宽。运行中改也会立刻生效。")]
    [Range(0.01f, 1f)] public float fulcrumTopWidth = 0.1f;

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

    [Header("敌人")]
    [Tooltip("进入攻击后，与更早生成的敌人保持的间距。")]
    public float enemyAttackSpacing = 0.3f;
    [Tooltip("敌人在生成点周围出现的半径。")]
    public float enemySpawnRadius = 1f;
    [Tooltip("敌人视觉与碰撞大小倍率。")]
    public float enemyScale = 2f;

    [Header("战斗")]
    [Tooltip("投掷物视觉大小倍率。")]
    public float projectileScale = 1.5f;

    [Header("摄像机")]
    [Tooltip("初始视野大小（正交尺寸），数值越小看得越近。")]
    public float cameraSize = 8f;
    [Tooltip("滚轮放大时的最小视野。")]
    public float cameraZoomMin = 3f;
    [Tooltip("滚轮缩小时的最大视野。")]
    public float cameraZoomMax = 16f;
    [Tooltip("滚轮每格缩放的幅度。")]
    public float cameraZoomSpeed = 1.2f;
}
