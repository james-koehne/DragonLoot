using System.Collections.Generic;

public struct SyncValue<T>
{
	public bool Setup { get; private set; }
	public T CurrentValue { get; private set; }
	public T OldValue { get; private set; }
	public bool Changed { get; private set; }
	public bool Dirty { get; private set; }

	public void Reset()
	{
		Setup = false;
		Changed = false;
		Dirty = false;
		CurrentValue = default;
		OldValue = default;
	}

	public void Update( T newVal )
	{
		if ( !Setup )
		{
			Setup = true;
			Changed = false;
			Dirty = true;
			CurrentValue = newVal;
			OldValue = default;
			return;
		}

		Changed = !EqualityComparer<T>.Default.Equals( newVal, CurrentValue );

		if ( Changed )
		{
			OldValue = CurrentValue;
			CurrentValue = newVal;
			Dirty = true;
		}
		else
		{
			Dirty = false;
		}
	}

	public static void Update( ref SyncValue<T> sync, T currentVal )
	{
		sync.Update( currentVal );
	}
}
