export interface SavedPcReconnectOption {
  id: string;
  label: string;
}

interface SavedPcReconnectChoiceProps {
  onChange: (pcId: string) => void;
  options: SavedPcReconnectOption[];
  selectedPcId?: string | undefined;
}

export function SavedPcReconnectChoice({ onChange, options, selectedPcId }: SavedPcReconnectChoiceProps) {
  if (options.length <= 1) {
    return null;
  }

  return (
    <label className="pairing-saved-pc-choice">
      <span>Saved PC</span>
      <select
        className="text-input"
        value={selectedPcId}
        onChange={(event) => { onChange(event.target.value); }}
      >
        {options.map((pc) => <option key={pc.id} value={pc.id}>{pc.label}</option>)}
      </select>
    </label>
  );
}
