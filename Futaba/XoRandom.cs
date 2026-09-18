using System.Numerics;

namespace Futaba;

// We want RNG to never change across builds
// but we also want the benefit of xoshiro
// so this is a handrolled copy of that algorithm
// but with custom seeding
// most of this is copied from <https://github.com/dotnet/dotnet/blob/main/src/runtime/src/libraries/System.Private.CoreLib/src/System/Random.Xoshiro256StarStarImpl.cs>
// which is licensed under the MIT license
//       Copyright (c) .NET Foundation and Contributors
//
//       Permission is hereby granted, free of charge, to any person obtaining a copy
//       of this software and associated documentation files (the "Software"), to deal
//       in the Software without restriction, including without limitation the rights
//       to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//       copies of the Software, and to permit persons to whom the Software is
//       furnished to do so, subject to the following conditions:
//
//       The above copyright notice and this permission notice shall be included in all
//       copies or substantial portions of the Software.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
internal unsafe class XoRandom {
	private ulong rng0, rng1, rng2, rng3;

	public XoRandom() {

	}

	public XoRandom(UInt128 seed) {
		Reseed(seed);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Reseed(UInt128 seed) {
		if (BitConverter.IsLittleEndian) {
			uint* sptr = (uint*) &seed;
			rng0 = sptr[0];
			rng1 = sptr[1];
			rng2 = sptr[2];
			rng3 = sptr[3];
		} else {
			rng0 = (uint) (seed >> 0);
			rng1 = (uint) (seed >> 32);
			rng2 = (uint) (seed >> 64);
			rng3 = (uint) (seed >> 96);
		}


		
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ulong NextUInt64() {
		ulong s0 = rng0;
		ulong s1 = rng1;
		ulong s2 = rng2;
		ulong s3 = rng3;

		ulong result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
		ulong t = s1 << 17;

		s2 ^= s0;
		s3 ^= s1;
		s1 ^= s2;
		s0 ^= s3;

		s2 ^= t;
		s3 = BitOperations.RotateRight(s3, 19);

		rng0 = s0;
		rng1 = s1;
		rng2 = s2;
		rng3 = s3;

		return result;
	}

	public void NextBytes(Span<byte> buffer) {
		ulong s0 = rng0;
		ulong s1 = rng1;
		ulong s2 = rng2;
		ulong s3 = rng3;

		while (buffer.Length >= sizeof(ulong)) {
			MemoryMarshal.Write(buffer, BitOperations.RotateLeft(s1 * 5, 7) * 9);

			ulong t = s1 << 17;
			s2 ^= s0;
			s3 ^= s1;
			s1 ^= s2;
			s0 ^= s3;
			s2 ^= t;
			s3 = BitOperations.RotateRight(s3, 19);

			buffer = buffer.Slice(sizeof(ulong));
		}

		if (!buffer.IsEmpty) {
			ulong next = BitOperations.RotateLeft(s1 * 5, 7) * 9;

			byte* remainingBytes = (byte*) &next;

			Debug.Assert(buffer.Length < sizeof(ulong));

			for (int i = 0; i < buffer.Length; i++) {
				buffer[i] = remainingBytes[i];
			}

			ulong t = s1 << 17;
			s2 ^= s0;
			s3 ^= s1;
			s1 ^= s2;
			s0 ^= s3;
			s2 ^= t;
			s3 = BitOperations.RotateRight(s3, 19);
		}

		rng0 = s0;
		rng1 = s1;
		rng2 = s2;
		rng3 = s3;
	}

	public long NextInt64(long minValue, long maxValue) {
		ulong exclusiveRange = (ulong) (maxValue - minValue);

		if (exclusiveRange <= int.MaxValue) {
			return NextUInt32((uint) exclusiveRange) + minValue;
		}

		if (exclusiveRange > 1) {
			// Narrow down to the smallest range [0, 2^bits] that contains maxValue.
			// Then repeatedly generate a value in that outer range until we get one within the inner range.
			int bits = Log2Ceiling(exclusiveRange);

			while (true) {
				ulong result = NextUInt64() >> (sizeof(ulong) * 8 - bits);

				if (result < exclusiveRange) {
					return (long) result + minValue;
				}
			}
		}

		return minValue;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal uint NextUInt32(uint maxValue) {
		ulong randomProduct = (ulong) maxValue * (uint) NextUInt64();
		uint lowPart = (uint) randomProduct;

		if (lowPart < maxValue) {
			uint remainder = (0U - maxValue) % maxValue;

			while (lowPart < remainder) {
				randomProduct = (ulong) maxValue * (uint) NextUInt64();
				lowPart = (uint) randomProduct;
			}
		}

		return (uint) (randomProduct >> 32);
	}

	// copied from BitOperations.cs, which is internal, for some reason...
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Log2Ceiling(ulong value) {
		int result = BitOperations.Log2(value);
		if (BitOperations.PopCount(value) != 1) {
			result++;
		}
		return result;
	}

}
