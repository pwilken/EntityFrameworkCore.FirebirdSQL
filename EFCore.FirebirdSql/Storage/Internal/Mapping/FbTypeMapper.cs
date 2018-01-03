/*
 *          Copyright (c) 2017 Rafael Almeida (ralms@ralms.net)
 *                               Jiri Cincura   (juri@cincura.net)
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
using System.Collections.Generic;
using System.Data;
using EntityFrameworkCore.FirebirdSql.Extensions;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.FirebirdSql.Storage.Internal.Mapping
{
    public class FbTypeMapper : RelationalTypeMapper
    {
        public const int BinaryMaxSize = int.MaxValue;
        public const int VarcharMaxSize = 32765;
        public const int NVarcharMaxSize = VarcharMaxSize / 4;
        public const int DefaultDecimalPrecision = 18;
        public const int DefaultDecimalScale = 2;

        private readonly FbBoolTypeMapping _boolean = new FbBoolTypeMapping();

        private readonly ShortTypeMapping _smallint = new ShortTypeMapping("SMALLINT", DbType.Int16);
        private readonly IntTypeMapping _integer = new IntTypeMapping("INTEGER", DbType.Int32);
        private readonly LongTypeMapping _bigint = new LongTypeMapping("BIGINT", DbType.Int64);

        private readonly FbStringTypeMapping _char = new FbStringTypeMapping("CHAR", FbDbType.Char);
        private readonly FbStringTypeMapping _varchar = new FbStringTypeMapping("VARCHAR", FbDbType.VarChar);
        private readonly FbStringTypeMapping _varcharMax = new FbStringTypeMapping($"VARCHAR({VarcharMaxSize})", FbDbType.VarChar, size: VarcharMaxSize);
        private readonly FbStringTypeMapping _nvarcharMax = new FbStringTypeMapping($"VARCHAR({NVarcharMaxSize})", FbDbType.VarChar, size: NVarcharMaxSize);
        private readonly FbStringTypeMapping _varchar256 = new FbStringTypeMapping($"VARCHAR(256)", FbDbType.VarChar, size: 256);
        private readonly FbStringTypeMapping _nvarchar256 = new FbStringTypeMapping($"VARCHAR(256)", FbDbType.VarChar, size: 256);
        private readonly FbStringTypeMapping _clob = new FbStringTypeMapping("BLOB SUB_TYPE TEXT", FbDbType.Text);

        private readonly FbByteArrayTypeMapping _binary = new FbByteArrayTypeMapping();

        private readonly FloatTypeMapping _float = new FloatTypeMapping("FLOAT");
        private readonly DoubleTypeMapping _double = new DoubleTypeMapping("DOUBLE PRECISION");
        private readonly DecimalTypeMapping _decimal = new DecimalTypeMapping($"DECIMAL({DefaultDecimalPrecision},{DefaultDecimalScale})");

        private readonly FbDateTimeTypeMapping _timeStamp = new FbDateTimeTypeMapping("TIMESTAMP", FbDbType.TimeStamp);
        private readonly FbDateTimeTypeMapping _date = new FbDateTimeTypeMapping("DATE", FbDbType.Date);
        private readonly FbDateTimeTypeMapping _time = new FbDateTimeTypeMapping("TIME", FbDbType.Time);

        private readonly FbGuidTypeMapping _guid = new FbGuidTypeMapping();

        private readonly Dictionary<string, RelationalTypeMapping> _storeTypeMappings;
        private readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings;
        private readonly HashSet<string> _disallowedMappings;

        public FbTypeMapper(RelationalTypeMapperDependencies dependencies)
            : base(dependencies)
        {
            _storeTypeMappings = new Dictionary<string, RelationalTypeMapping>(StringComparer.OrdinalIgnoreCase)
            {
                { "BOOLEAN", _boolean },
                { "SMALLINT", _smallint },
                { "INTEGER", _integer },
                { "BIGINT", _bigint },
                { "CHAR", _char },
                { "VARCHAR", _varchar },
                { "BLOB SUB_TYPE TEXT", _clob },
                { "BLOB SUB_TYPE BINARY", _binary },
                { "FLOAT", _float },
                { "DOUBLE PRECISION", _double },
                { "DECIMAL", _decimal },
                { "NUMERIC", _decimal },
                { "TIMESTAMP", _timeStamp },
                { "DATE", _date },
                { "TIME", _time },
                { "CHAR(16) CHARACTER SET OCTETS", _guid },
            };

            _clrTypeMappings = new Dictionary<Type, RelationalTypeMapping>()
            {
                { typeof(bool), _boolean },
                { typeof(short), _smallint },
                { typeof(int), _integer },
                { typeof(long), _bigint },
                { typeof(float), _float },
                { typeof(double), _double},
                { typeof(decimal), _decimal },
                { typeof(byte[]), _binary },
                { typeof(DateTime), _timeStamp },
                { typeof(TimeSpan), _time },
                { typeof(Guid), _guid }
            };

            _disallowedMappings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                    "CHARACTER",
                    "CHAR",
                    "VARCHAR",
                    "CHARACTER VARYING",
                    "CHAR VARYING",
            };

            ByteArrayMapper = new ByteArrayRelationalTypeMapper(
                maxBoundedLength: BinaryMaxSize,
                defaultMapping: _binary,
                unboundedMapping: _binary,
                keyMapping: _binary,
                rowVersionMapping: null,
                createBoundedMapping: _ => _binary);

            StringMapper = new StringRelationalTypeMapper(
                maxBoundedAnsiLength: VarcharMaxSize,
                defaultAnsiMapping: _varcharMax,
                unboundedAnsiMapping: _varcharMax,
                keyAnsiMapping: _varchar256,
                createBoundedAnsiMapping: size => new FbStringTypeMapping($"VARCHAR({size})", FbDbType.VarChar, size),
                maxBoundedUnicodeLength: NVarcharMaxSize,
                defaultUnicodeMapping: _nvarcharMax,
                unboundedUnicodeMapping: _nvarcharMax,
                keyUnicodeMapping: _nvarchar256,
                createBoundedUnicodeMapping: size => new FbStringTypeMapping($"VARCHAR({size})", FbDbType.VarChar, size));
        }

        public override IByteArrayRelationalTypeMapper ByteArrayMapper { get; }

        public override IStringRelationalTypeMapper StringMapper { get; }

        public override void ValidateTypeName(string storeType)
        {
            if (_disallowedMappings.Contains(storeType))
            {
                throw new ArgumentException($"Data type '{storeType}' is invalid.");
            }
        }

        protected override string GetColumnType(IProperty property)
            => property.Firebird().ColumnType;

        protected override IReadOnlyDictionary<Type, RelationalTypeMapping> GetClrTypeMappings()
            => _clrTypeMappings;

        protected override IReadOnlyDictionary<string, RelationalTypeMapping> GetStoreTypeMappings()
            => _storeTypeMappings;

        public override RelationalTypeMapping FindMapping(Type clrType)
        {
            clrType = clrType.UnwrapNullableType().UnwrapEnumType();

            return clrType == typeof(string)
                ? _nvarcharMax
                : clrType == typeof(byte[])
                    ? _binary
                    : base.FindMapping(clrType);
        }

        protected override bool RequiresKeyMapping(IProperty property)
            => base.RequiresKeyMapping(property) || property.IsIndex();
    }
}
