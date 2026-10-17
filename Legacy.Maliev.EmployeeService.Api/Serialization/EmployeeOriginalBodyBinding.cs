using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Legacy.Maliev.EmployeeService.Api.Serialization;

// Per-request handoff, never a wire key, credential, global flag or model cache.
internal static class EmployeeOriginalBodyProjection
{
    internal static readonly object Key = new();
}

// The actual existing body provider is preserved at its original position.
internal sealed class EmployeeOriginalBodyModelBinderProvider : IModelBinderProvider
{
    private readonly IModelBinderProvider inner;

    internal EmployeeOriginalBodyModelBinderProvider(IModelBinderProvider inner) => this.inner = inner;

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var binder = inner.GetBinder(context);
        if (binder is null || !EmployeeOriginalBodyInputFormatter.IsSelected(context.Metadata.ModelType))
            return binder;
        return new EmployeeOriginalBodyModelBinder(binder);
    }
}

internal sealed class EmployeeOriginalBodyModelBinder(IModelBinder inner) : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext context)
    {
        var items = context.HttpContext.Items;
        items.Remove(EmployeeOriginalBodyProjection.Key);
        try
        {
            await inner.BindModelAsync(context);
            var result = context.Result;
            if (result.IsModelSet && result.Model is { } model
                && items.TryGetValue(EmployeeOriginalBodyProjection.Key, out var projected)
                && ReferenceEquals(model, projected))
            {
                // The audited original scaffold models are nullable-oblivious and
                // have no model-validation attributes. A target record's inferred
                // Required validators must not add a new pre-controller phase.
                // This suppresses ALL validation for this exact projected object;
                // the regression guard rejects future explicit attribute drift.
                context.ValidationState[model] = new ValidationStateEntry
                {
                    Key = context.ModelName,
                    SuppressValidation = true
                };
            }
        }
        finally
        {
            items.Remove(EmployeeOriginalBodyProjection.Key);
        }
    }
}
