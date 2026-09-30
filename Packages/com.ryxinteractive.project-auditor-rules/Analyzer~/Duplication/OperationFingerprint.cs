// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace RyxInteractive.ProjectAuditorRules;

// IDs need only be consistent within one compilation, never persisted or displayed.
// Symbol equality preserves overloads, constructed types and assembly identity.
internal sealed class FingerprintSymbols
{
    private readonly ConcurrentDictionary<ISymbol, int> ids =
        new ConcurrentDictionary<ISymbol, int>(SymbolEqualityComparer.Default);
    private int next;
    internal int Id(ISymbol symbol) => symbol == null ? 0 :
        ids.GetOrAdd(symbol, _ => Interlocked.Increment(ref next));
}

internal sealed class OperationFingerprint
{
    internal const int MinimumExplicitOperations = 40;
    private readonly IMethodSymbol method;
    private readonly FingerprintSymbols symbols;
    private readonly CancellationToken cancellationToken;
    private readonly Dictionary<ILocalSymbol, int> locals =
        new Dictionary<ILocalSymbol, int>(SymbolEqualityComparer.Default);
    private readonly StringBuilder builder = new StringBuilder();
    private int explicitOperations;
    private bool hasControlFlow;

    private OperationFingerprint(IMethodSymbol method, FingerprintSymbols symbols, CancellationToken cancellationToken)
    {
        this.method = method;
        this.symbols = symbols;
        this.cancellationToken = cancellationToken;
    }

    internal static bool TryCreate(IMethodSymbol method, ImmutableArray<IOperation> blocks,
        FingerprintSymbols symbols, CancellationToken cancellationToken, out string fingerprint)
    {
        fingerprint = null;
        var writer = new OperationFingerprint(method, symbols, cancellationToken);
        writer.Add(symbols.Id(method.ReturnType));
        writer.Add(method.RefKind);
        foreach (var parameter in method.Parameters)
        {
            writer.Add(symbols.Id(parameter.Type));
            writer.Add(parameter.RefKind);
        }
        writer.Add("body");
        foreach (var block in blocks)
            if (!writer.Visit(block))
                return false;
        if (!writer.hasControlFlow || writer.explicitOperations < MinimumExplicitOperations)
            return false;
        fingerprint = writer.builder.ToString();
        return true;
    }

    private bool Visit(IOperation operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Type?.TypeKind == TypeKind.Dynamic)
            return false;
        builder.Append('[');
        Add(operation.Kind);
        Add(symbols.Id(operation.Type));
        Add(operation.IsImplicit);
        if (!operation.IsImplicit && !(operation is IBlockOperation))
            explicitOperations++;
        if (operation.ConstantValue.HasValue)
        {
            Add("constant");
            Add(operation.ConstantValue.Value == null ? "null" :
                Convert.ToString(operation.ConstantValue.Value, CultureInfo.InvariantCulture));
        }
        switch (operation)
        {
            case IBlockOperation block:
                foreach (var local in block.Locals)
                    Declare(local);
                break;
            case IVariableDeclarationGroupOperation _:
            case IVariableDeclarationOperation _:
            case IVariableInitializerOperation _:
            case IExpressionStatementOperation _:
                break;
            case IReturnOperation returned:
                if (returned.Kind != OperationKind.Return)
                    return false;
                break;
            case ILiteralOperation _:
            case IEmptyOperation _:
            case IDefaultValueOperation _:
            case IArrayElementReferenceOperation _:
            case IArrayCreationOperation _:
            case IArrayInitializerOperation _:
            case IParenthesizedOperation _:
                break;
            case IVariableDeclaratorOperation declarator:
                Add(Declare(declarator.Symbol));
                Add(symbols.Id(declarator.Symbol.Type));
                Add(declarator.Symbol.RefKind); Add(declarator.Symbol.IsConst);
                break;
            case ILocalReferenceOperation local:
                if (!locals.TryGetValue(local.Local, out var id))
                    return false;
                Add(id);
                Add(local.IsDeclaration);
                break;
            case IParameterReferenceOperation parameter:
                Add(parameter.Parameter.Ordinal);
                break;
            case IBinaryOperation binary:
                Add(binary.OperatorKind); Add(binary.IsChecked); Add(binary.IsLifted);
                Add(symbols.Id(binary.OperatorMethod));
                break;
            case IUnaryOperation unary:
                Add(unary.OperatorKind); Add(unary.IsChecked); Add(unary.IsLifted);
                Add(symbols.Id(unary.OperatorMethod));
                break;
            case IConversionOperation conversion:
                Add(conversion.IsChecked); Add(conversion.IsTryCast);
                Conversion(conversion.Conversion);
                break;
            case ISimpleAssignmentOperation assignment:
                Add(assignment.IsRef);
                break;
            case ICompoundAssignmentOperation compound:
                Add(compound.OperatorKind); Add(compound.IsChecked); Add(compound.IsLifted);
                Add(symbols.Id(compound.OperatorMethod));
                Conversion(compound.InConversion); Conversion(compound.OutConversion);
                break;
            case IIncrementOrDecrementOperation increment:
                Add(increment.IsPostfix); Add(increment.IsChecked); Add(increment.IsLifted);
                Add(symbols.Id(increment.OperatorMethod));
                break;
            case IConditionalOperation conditional:
                hasControlFlow = true;
                Add(conditional.IsRef);
                break;
            case IForLoopOperation loop:
                hasControlFlow = true;
                foreach (var local in loop.Locals)
                    Declare(local);
                break;
            case IWhileLoopOperation loop:
                hasControlFlow = true;
                Add(loop.ConditionIsTop); Add(loop.ConditionIsUntil);
                break;
            case IBranchOperation branch:
                // goto targets require a separate label mapping. Skip the method.
                if (branch.BranchKind == BranchKind.GoTo)
                    return false;
                Add(branch.BranchKind);
                break;
            case IInvocationOperation invocation:
                if (SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, method) ||
                    HasCallerInfo(invocation.TargetMethod))
                    return false;
                Add(symbols.Id(invocation.TargetMethod)); Add(invocation.IsVirtual);
                break;
            case IObjectCreationOperation creation:
                if (creation.Constructor == null || HasCallerInfo(creation.Constructor))
                    return false;
                Add(symbols.Id(creation.Constructor));
                break;
            case IArgumentOperation argument:
                Add(argument.ArgumentKind); Add(argument.Parameter?.Ordinal ?? -1);
                Conversion(argument.InConversion); Conversion(argument.OutConversion);
                break;
            case IFieldReferenceOperation field:
                Add(symbols.Id(field.Field));
                break;
            case IPropertyReferenceOperation property:
                Add(symbols.Id(property.Property));
                break;
            default:
                // In particular: invalid/dynamic operations, nameof, lambdas, local
                // functions, foreach, yield, await, patterns and unsafe constructs.
                return false;
        }
        foreach (var child in operation.ChildOperations)
            if (!Visit(child))
                return false;
        builder.Append(']');
        return true;
    }

    private int Declare(ILocalSymbol local)
    {
        if (!locals.TryGetValue(local, out var id))
        {
            id = locals.Count;
            locals.Add(local, id);
        }
        return id;
    }

    private void Conversion(CommonConversion conversion)
    {
        Add(conversion.Exists); Add(conversion.IsIdentity); Add(conversion.IsNumeric);
        Add(conversion.IsReference); Add(conversion.IsNullable); Add(conversion.IsUserDefined);
        Add(symbols.Id(conversion.MethodSymbol));
    }

    private static bool HasCallerInfo(IMethodSymbol target) => target.Parameters.Any(parameter =>
        parameter.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices" &&
            (attribute.AttributeClass.Name == "CallerMemberNameAttribute" ||
             attribute.AttributeClass.Name == "CallerFilePathAttribute" ||
             attribute.AttributeClass.Name == "CallerLineNumberAttribute" ||
             attribute.AttributeClass.Name == "CallerArgumentExpressionAttribute")));

    // Length-prefix every atom, so string literals cannot imitate delimiters.
    private void Add(object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        builder.Append(text.Length).Append(':').Append(text);
    }
}
