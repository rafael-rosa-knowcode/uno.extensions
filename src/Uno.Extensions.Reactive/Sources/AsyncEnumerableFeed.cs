﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Uno.Extensions.Reactive.Core;

namespace Uno.Extensions.Reactive.Sources;

internal sealed class AsyncEnumerableFeed<T> : IFeed<T>
{
	private readonly Func<CancellationToken, IAsyncEnumerable<Option<T>>> _factory;

	public AsyncEnumerableFeed(Func<IAsyncEnumerable<T>> factoryWithoutOptions)
	{
		_factory = Factory;

		async IAsyncEnumerable<Option<T>> Factory([EnumeratorCancellation] CancellationToken ct)
		{
			await foreach (var item in factoryWithoutOptions().WithCancellation(ct).ConfigureAwait(false))
			{
				yield return Option.SomeOrNone(item);
			}
		}
	}

	public AsyncEnumerableFeed(Func<CancellationToken, IAsyncEnumerable<T>> factoryWithoutOptions)
	{
		_factory = Factory;

		async IAsyncEnumerable<Option<T>> Factory([EnumeratorCancellation] CancellationToken ct)
		{
			await foreach (var item in factoryWithoutOptions(ct).WithCancellation(ct).ConfigureAwait(false))
			{
				yield return Option.SomeOrNone(item);
			}
		}
	}

	public AsyncEnumerableFeed(Func<IAsyncEnumerable<Option<T>>> factory)
	{
		_factory = ct => factory();
	}

	public AsyncEnumerableFeed(Func<CancellationToken, IAsyncEnumerable<Option<T>>> factory)
	{
		_factory = factory;
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<Message<T>> GetSource(SourceContext context, [EnumeratorCancellation] CancellationToken ct = default)
	{
		using var _ = context.AsCurrent();

		var isFirstMessage = true;
		var current = Message<T>.Initial;

		IAsyncEnumerator<Option<T>>? enumerator = default;
		Exception? error = default;
		bool hasCurrent;
		do
		{
			try
			{
				enumerator ??= _factory(ct).GetAsyncEnumerator(ct);
				hasCurrent = await enumerator.MoveNextAsync().ConfigureAwait(false);
			}
			catch (Exception e)
			{
				error = e;
				hasCurrent = false; // Safety only
			}

			if (error is not null)
			{
				yield return current.With().Error(error).IsTransient(false);
				yield break;
			}

			if (hasCurrent)
			{
				var updated = current.With().Data(enumerator!.Current).Build();
				if (isFirstMessage || updated is { Changes.Count: > 0 })
				{
					yield return current = updated;
					isFirstMessage = false;
				}
			}
		} while (hasCurrent);

		if (isFirstMessage)
		{
			// Make sure to at least play en empty message
			yield return current;
		}
	}
}
