namespace KnowledgePortal.Api.Helpers;

public static class VectorMath
{
    public static double ComputeNorm(float[] vector)
    {
        double sum = 0;
        for (int i = 0; i < vector.Length; i++)
            sum += (double)vector[i] * vector[i];
        return Math.Sqrt(sum);
    }
}
