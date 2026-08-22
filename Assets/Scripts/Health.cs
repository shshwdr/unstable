using UnityEngine;

public class Health : MonoBehaviour
{
    public float MaxHp { get; private set; }
    public float CurrentHp { get; private set; }

    public bool IsAlive
    {
        get { return this != null && CurrentHp > 0f; }
    }

    Vector3 barOffset;
    Color barColor = new Color(0.25f, 0.85f, 0.35f, 1f);
    HpBar hpBar;

    public void Init(float hp, Vector3 barOffset)
    {
        Init(hp, barOffset, new Color(0.25f, 0.85f, 0.35f, 1f));
    }

    public void Init(float hp, Vector3 barOffset, Color barColor)
    {
        MaxHp = Mathf.Max(1f, hp);
        CurrentHp = MaxHp;
        this.barOffset = barOffset;
        this.barColor = barColor;
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || amount <= 0f)
            return;

        CurrentHp = Mathf.Max(0f, CurrentHp - amount);
        if (hpBar == null)
            hpBar = HpBar.Create(transform, barOffset, barColor);
        hpBar.Set(CurrentHp, MaxHp);

        if (CurrentHp <= 0f)
            Destroy(gameObject);
    }
}
