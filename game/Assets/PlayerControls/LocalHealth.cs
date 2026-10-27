using UnityEngine;

// Add to an enemy or destructible root; child colliders forward hits to this component.
public class LocalHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    public float CurrentHealth { get; private set; }

    private void Awake() => CurrentHealth = maxHealth;

    public void TakeDamage(float damage)
    {
        if (damage <= 0f || CurrentHealth <= 0f)
            return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        if (CurrentHealth == 0f)
            gameObject.SetActive(false);
    }
}
