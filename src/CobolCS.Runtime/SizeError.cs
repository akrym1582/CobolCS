namespace CobolCS.Runtime;

/// <summary>
/// 計算結果を受け取る PIC の桁数や符号の制約に収められないことを表します。
/// 通常の実行失敗とは区別し、COBOL の ON SIZE ERROR に制御を渡すために使います。
/// </summary>
/// <param name="message">格納できなかった項目の修飾名など、原因を調べるための情報。</param>
public sealed class SizeError(string message) : Exception(message);
