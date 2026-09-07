namespace HLab.Geo;

[Serializable]
public readonly struct Vector(double x, double y) : IFormattable
{
   public double Length => Math.Sqrt(LengthSquared);
   public double LengthSquared => this * this;

   public Vector Normalize() => (this / Math.Max(Math.Abs(X), Math.Abs(Y))) /Length;

   public static double CrossProduct(Vector vector1, Vector vector2) => vector1.X * vector2.Y - vector1.Y * vector2.X;

   public static double AngleBetween(Vector vector1, Vector vector2)
   {
      var sin = CrossProduct(vector1, vector2);
      var cos = vector1 * vector2;

      return Math.Atan2(sin, cos) * (180 / Math.PI);
   }

   public static Vector Negate(Vector vector) => new(-vector.X,-vector.Y);
   public static Vector Add(Vector vector1, Vector vector2) => new(vector1.X + vector2.X, vector1.Y + vector2.Y);
   public static Point Add(Vector vector, Point point) => new(point.X + vector.X, point.Y + vector.Y);
   public static Vector Subtract(Vector vector1, Vector vector2) => new(vector1.X - vector2.X, vector1.Y - vector2.Y);
   public static Vector Multiply(Vector vector, double scalar) => new(vector.X * scalar, vector.Y * scalar);
   public static Vector Multiply(double scalar, Vector vector) => Multiply(vector, scalar);
   public static Vector Multiply(Vector vector, Matrix matrix) => matrix.Transform(vector);
   public static double DotMultiply(Vector vector1, Vector vector2) => vector1.X * vector2.X + vector1.Y * vector2.Y;
   public static Vector Divide(Vector vector, double scalar) => vector * (1.0 / scalar);
   public static Vector ComponentDivide(Vector vector1, Vector vector2) => new(vector1.X / vector2.X, vector1.Y / vector2.Y);
    public static double Determinant(Vector vector1, Vector vector2) => CrossProduct(vector1, vector2);
   public static Vector ComponentMultiply(Vector vector1, Vector vector2) => new(vector1.X * vector2.X, vector1.Y * vector2.Y);

   public static Vector operator -(Vector vector) => Negate(vector);
   public static Vector operator +(Vector vector1, Vector vector2) => Add(vector1,vector2);
   public static Vector operator -(Vector vector1, Vector vector2) => Subtract(vector1,vector2);
   public static Point operator +(Vector vector, Point point) => Add(vector,point);
   public static Vector operator *(Vector vector, double scalar) => Multiply(vector, scalar);
   public static Vector operator *(double scalar, Vector vector) => Multiply(scalar, vector);
   public Vector ComponentMultiply(Vector vector) => ComponentMultiply(this,vector);

   public static Vector operator /(Vector vector, double scalar) => Divide(vector, scalar);
   public static Vector operator /(Vector vector1, Vector vector2) => ComponentDivide(vector1, vector2);
   public static Vector operator *(Vector vector, Matrix matrix) => Multiply(vector, matrix);
   public static double operator *(Vector vector1, Vector vector2) => DotMultiply(vector1, vector2);

   public static explicit operator Size(Vector vector) => new(Math.Abs(vector.X), Math.Abs(vector.Y));
   public static explicit operator Point(Vector vector) => new(vector.X, vector.Y);

   // ReSharper disable CompareOfFloatsByEqualityOperator
   public static bool operator ==(Vector vector1, Vector vector2) => vector1.X == vector2.X && vector1.Y == vector2.Y;
   public static bool operator !=(Vector vector1, Vector vector2) => !(vector1 == vector2);

   public static bool Equals(Vector vector1, Vector vector2) => vector1.X.Equals(vector2.X) && vector1.Y.Equals(vector2.Y);
   public override bool Equals(object? o) => o is Vector value && Equals(this, value);
   public bool Equals(Vector value) => Equals(this, value);

   public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();

   public double X => x;
   public double Y => y;

   public override string ToString() => ConvertToString(null /* format string */, null /* format provider */);
   public string ToString(IFormatProvider provider) => ConvertToString(null /* format string */, provider);
   string IFormattable.ToString(string format, IFormatProvider provider) => ConvertToString(format, provider);

   internal string ConvertToString(string format, IFormatProvider provider)
   {
      const char separator = ','; 
      return string.Format(provider,
          "{1:" + format + "}{0}{2:" + format + "}",
          separator,
          X,
          Y);
   }

}