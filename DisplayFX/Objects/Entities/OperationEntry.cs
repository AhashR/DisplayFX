using System;
namespace DisplayFX.Objects.Entities;
public enum OperationSeverity { Information, Success, Warning, Error }
public sealed record OperationEntry(DateTimeOffset Time, OperationSeverity Severity, string Message);
