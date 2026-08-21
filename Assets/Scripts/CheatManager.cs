using UnityEngine;

[DefaultExecutionOrder(-100)]
public class CheatManager : MonoBehaviour
{
    BalanceWorld world;
    EncounterManager encounters;

    void Awake()
    {
        world = GetComponent<BalanceWorld>();
        encounters = GetComponent<EncounterManager>();
    }

    void Update()
    {
        if (world == null)
            return;

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (shift)
        {
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    world.JumpToLevel(i);
                    return;
                }
            }
        }

        if (TutorialManager.Instance != null && TutorialManager.Instance.BlocksInput)
            return;

        if (Input.GetKeyDown(KeyCode.K))
            Enemy.ClearAll();

        if (Input.GetKeyDown(KeyCode.L) && encounters != null)
            encounters.CompleteAll();
    }
}
