using UnityEngine;
using UnityEngine.UI;

public class ProgressBar : MonoBehaviour
{
    public Image image;

    public float amount
    {
        get { return image != null ? image.fillAmount : 0f; }
        set
        {
            if (image != null)
                image.fillAmount = Mathf.Clamp01(value);
        }
    }
}
