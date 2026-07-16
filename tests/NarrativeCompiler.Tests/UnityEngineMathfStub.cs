namespace UnityEngine;

public static class Mathf
{
    public static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);

    public static float Clamp(float value, float minimum, float maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);

    public static float Clamp01(float value) => Clamp(value, 0f, 1f);

    public static int Max(int left, int right) => Math.Max(left, right);

    public static float Max(float left, float right) => Math.Max(left, right);
}
