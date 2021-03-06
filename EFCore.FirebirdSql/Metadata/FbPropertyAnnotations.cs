/*
 *          Copyright (c) 2017 Rafael Almeida (ralms@ralms.net)
 *
 *                    EntityFrameworkCore.FirebirdSql
 *
 * THIS MATERIAL IS PROVIDED AS IS, WITH ABSOLUTELY NO WARRANTY EXPRESSED
 * OR IMPLIED.  ANY USE IS AT YOUR OWN RISK.
 * 
 * Permission is hereby granted to use or copy this program
 * for any purpose,  provided the above notices are retained on all copies.
 * Permission to modify the code and to distribute modified code is granted,
 * provided the above notices are retained, and a notice that the code was
 * modified is included with the above copyright notice.
 *
 */

using System;
using EntityFrameworkCore.FirebirdSql.Extensions;
using EntityFrameworkCore.FirebirdSql.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.FirebirdSql.Metadata
{
    public class FbPropertyAnnotations : RelationalPropertyAnnotations, IFbPropertyAnnotations
    {
        public FbPropertyAnnotations(IProperty property)
            : base(property)
        { }

        protected FbPropertyAnnotations(RelationalAnnotations annotations)
            : base(annotations)
        { }

        public virtual FbValueGenerationStrategy? ValueGenerationStrategy
        {
            get => GetValueGenerationStrategy(fallbackToModel: true);
            set => SetValueGenerationStrategy(value);
        }

        public virtual FbValueGenerationStrategy? GetValueGenerationStrategy(bool fallbackToModel)
        {
            var value = (FbValueGenerationStrategy?)Annotations.Metadata[FbAnnotationNames.ValueGenerationStrategy];

            if (value != null)
            {
                return value;
            }

            var relationalProperty = Property.Relational();
            if (!fallbackToModel
                || Property.ValueGenerated != ValueGenerated.OnAdd
                || relationalProperty.DefaultValue != null
                || relationalProperty.DefaultValueSql != null
                || relationalProperty.ComputedColumnSql != null)
            {
                return null;
            }

            var modelStrategy = Property.DeclaringEntityType.Model.Firebird().ValueGenerationStrategy;

            if (modelStrategy == FbValueGenerationStrategy.IdentityColumn && IsCompatibleIdentityColumn(Property.ClrType))
            {
                return FbValueGenerationStrategy.IdentityColumn;
            }

            if (modelStrategy == FbValueGenerationStrategy.SequenceTrigger && IsCompatibleSequenceTrigger(Property.ClrType))
            {
                return FbValueGenerationStrategy.SequenceTrigger;
            }
            return null;
        }

        protected virtual bool SetValueGenerationStrategy(FbValueGenerationStrategy? value)
        {
            if (value != null)
            {
                var propertyType = Property.ClrType;

                if (value == FbValueGenerationStrategy.IdentityColumn && !IsCompatibleIdentityColumn(propertyType))
                {
                    if (ShouldThrowOnInvalidConfiguration)
                    {
                        throw new ArgumentException($"Incompatible data type for ${nameof(FbValueGenerationStrategy.IdentityColumn)} for '{Property.Name}'.");
                    }

                    return false;
                }

                if (value == FbValueGenerationStrategy.SequenceTrigger && !IsCompatibleSequenceTrigger(propertyType))
                {
                    if (ShouldThrowOnInvalidConfiguration)
                    {
                        throw new ArgumentException($"Incompatible data type for ${nameof(FbValueGenerationStrategy.SequenceTrigger)} for '{Property.Name}'.");
                    }
                    return false;
                }
            }

            if (!CanSetValueGenerationStrategy(value))
            {
                return false;
            }

            if (!ShouldThrowOnConflict
                && ValueGenerationStrategy != value
                && value != null)
            {
                ClearAllServerGeneratedValues();
            }

            return Annotations.SetAnnotation(FbAnnotationNames.ValueGenerationStrategy, value);
        }

        protected virtual bool CanSetValueGenerationStrategy(FbValueGenerationStrategy? value)
        {
            if (GetValueGenerationStrategy(fallbackToModel: false) == value)
            {
                return true;
            }

            if (!Annotations.CanSetAnnotation(FbAnnotationNames.ValueGenerationStrategy, value))
            {
                return false;
            }

            if (ShouldThrowOnConflict)
            {
                if (GetDefaultValue(false) != null)
                {
                    throw new InvalidOperationException(RelationalStrings.ConflictingColumnServerGeneration(nameof(ValueGenerationStrategy), Property.Name, nameof(DefaultValue)));
                }
                if (GetDefaultValueSql(false) != null)
                {
                    throw new InvalidOperationException(RelationalStrings.ConflictingColumnServerGeneration(nameof(ValueGenerationStrategy), Property.Name, nameof(DefaultValueSql)));
                }
                if (GetComputedColumnSql(false) != null)
                {
                    throw new InvalidOperationException(RelationalStrings.ConflictingColumnServerGeneration(nameof(ValueGenerationStrategy), Property.Name, nameof(ComputedColumnSql)));
                }
            }
            else if (value != null
                && (!CanSetDefaultValue(null)
                || !CanSetDefaultValueSql(null)
                || !CanSetComputedColumnSql(null)))
            {
                return false;
            }

            return true;
        }

        private static bool IsCompatibleIdentityColumn(Type type)
            => type.IsInteger() || type == typeof(decimal);

        private static bool IsCompatibleSequenceTrigger(Type type)
            => true;
    }
}
