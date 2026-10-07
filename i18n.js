// UI strings. Add a language = add a block here (and a button in settings.html).
module.exports = {
  it: {
    tray: { alwaysOnTop: 'Sempre in primo piano', settings: 'Impostazioni...', showPet: 'Mostra gatto', hidePet: 'Nascondi gatto', exit: 'Esci', tooltip: 'Desktop Pet' },
    title: 'Impostazioni',
    navGeneral: 'Generale',
    navCat: 'Gatto',
    language: 'Lingua',
    languageHint: 'Lingua dell\'interfaccia',
    reset: 'Ripristina predefiniti',
    catIntro: 'Regola il comportamento del gatto. Le modifiche si applicano subito.',
    groups: { mouse: 'Mouse', movement: 'Movimento' },
    fields: {
      nearRange:    { label: 'Distanza "mouse vicino"', hint: 'Entro questa distanza il gatto si accorge del mouse', unit: 'px' },
      jumpSpeed:    { label: 'Velocità del mouse per il salto', hint: 'Più basso = salta più facilmente', unit: 'px/s' },
      jumpCooldown: { label: 'Pausa tra un salto e l\'altro', hint: '', unit: 's' },
      wander:       { label: 'Voglia di girare', hint: 'Probabilità al secondo di partire per una passeggiata (0 = sta fermo)', unit: '% al secondo' },
      walkSpeed:    { label: 'Velocità di camminata', hint: 'La corsa è il doppio', unit: 'px' },
      cycle:        { label: 'Durata del ciclo delle animazioni', hint: 'Più alto = animazioni più lente', unit: 'ms' }
    }
  },
  en: {
    tray: { alwaysOnTop: 'Always on top', settings: 'Settings...', showPet: 'Show pet', hidePet: 'Hide pet', exit: 'Exit', tooltip: 'Desktop Pet' },
    title: 'Settings',
    navGeneral: 'General',
    navCat: 'Cat',
    language: 'Language',
    languageHint: 'Interface language',
    reset: 'Reset to defaults',
    catIntro: 'Tune how the cat behaves. Changes apply immediately.',
    groups: { mouse: 'Mouse', movement: 'Movement' },
    fields: {
      nearRange:    { label: '"Mouse nearby" distance', hint: 'Within this distance the cat notices the mouse', unit: 'px' },
      jumpSpeed:    { label: 'Mouse speed to trigger a jump', hint: 'Lower = jumps more easily', unit: 'px/s' },
      jumpCooldown: { label: 'Pause between jumps', hint: '', unit: 's' },
      wander:       { label: 'Urge to wander', hint: 'Chance per second of going for a walk (0 = stays still)', unit: '% per second' },
      walkSpeed:    { label: 'Walking speed', hint: 'Running is twice as fast', unit: 'px' },
      cycle:        { label: 'Animation cycle length', hint: 'Higher = slower animations', unit: 'ms' }
    }
  }
};
