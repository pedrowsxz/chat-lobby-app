// Lobby chat client. Plain jQuery + the SignalR browser client, no build step.
// Rule for this file: user text goes into the DOM only through .text() or attributes, never .html().
(function () {
    'use strict';

    const me = $('meta[name="current-user"]').attr('content');
    if (!me) return; // not signed in

    const TIME_ZONE = 'America/Sao_Paulo';
    const $pane = $('#chat-pane');
    const conversationId = $pane.length ? Number($pane.data('conversation-id')) : null;
    const $scroller = $('#message-scroller');
    const $list = $('#message-list');
    const $input = $('#composer-input');
    let loadingOlder = false;

    // ---------- Small helpers ----------
    // crypto.randomUUID only exists on https and localhost. Elsewhere, build a version-4 UUID
    // by hand: 16 random bytes, two of them adjusted to mark "version 4, standard variant".
    function newId() {
        if (crypto.randomUUID) return crypto.randomUUID();
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        const hex = [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }

    const dayKey = date => new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
    const timeText = date => date.toLocaleTimeString('pt-BR', { timeZone: TIME_ZONE, hour: '2-digit', minute: '2-digit' });
    const fullText = date => date.toLocaleString('pt-BR', { timeZone: TIME_ZONE, dateStyle: 'full', timeStyle: 'short' });

    function dayLabel(key) {
        const today = dayKey(new Date());
        const yesterday = dayKey(new Date(Date.now() - 86400000));
        if (key === today) return 'Hoje';
        if (key === yesterday) return 'Ontem';
        const [y, m, d] = key.split('-').map(Number);
        return new Date(y, m - 1, d).toLocaleDateString('pt-BR', { day: 'numeric', month: 'long', year: 'numeric' });
    }

    // Find by data attribute with a plain comparison instead of building a CSS selector from data.
    const byData = ($items, name, value) => $items.filter((_, el) => el.dataset[name] === String(value));

    function nearBottom() {
        const el = $scroller[0];
        return el.scrollHeight - el.scrollTop - el.clientHeight < 120;
    }

    function scrollToBottom() {
        if ($scroller.length) $scroller.scrollTop($scroller[0].scrollHeight);
    }

    function markRead() {
        if (!conversationId) return;
        $.post(`/c/${conversationId}/read`);
        byData($('#conversation-list > .conversation-item'), 'conversationId', conversationId)
            .find('.unread-badge').attr({ 'data-count': 0, 'aria-label': '0 não lidas' }).text(0);
    }

    // After any insert: keep one separator per day and mark the first message of each run.
    function normalize() {
        const seenDays = new Set();
        let previousDay = null, previousSender = null;
        $list.children().each(function () {
            const el = this;
            if (el.classList.contains('day-sep')) {
                if (seenDays.has(el.dataset.day)) { el.remove(); return; }
                seenDays.add(el.dataset.day);
                return;
            }
            el.classList.toggle('first', el.dataset.day !== previousDay || el.dataset.sender !== previousSender);
            previousDay = el.dataset.day;
            previousSender = el.dataset.sender;
        });
        $('#no-messages').toggleClass('d-none', $list.children('.msg').length > 0);
    }

    // ---------- Rendering a message (same structure as _Messages.cshtml) ----------
    function buildMessage(m) {
        const sentAt = new Date(m.sentAt);
        const key = dayKey(sentAt);
        const $item = $(document.getElementById('message-template').content.firstElementChild.cloneNode(true));
        $item.toggleClass('mine', m.senderId === me)
            .attr({ 'data-cmid': m.clientMessageId, 'data-sender': m.senderId, 'data-day': key });
        if (m.id) $item.attr('data-id', m.id);
        $item[0].style.setProperty('--hue', m.senderHue);
        const $avatar = $item.find('.avatar'); // its colour comes from --hue on the <li>
        if (m.senderAvatarUrl) $avatar.append($('<img>').attr({ src: m.senderAvatarUrl, alt: '', loading: 'lazy' }));
        else $avatar.text(m.senderInitials);
        $item.find('.msg-sender').text(m.senderDisplayName);
        $item.find('.msg-body').text(m.body);
        $item.find('.msg-time').attr({ datetime: m.sentAt, title: fullText(sentAt) }).text(timeText(sentAt));
        return $item;
    }

    function appendMessage($item) {
        const key = $item.attr('data-day');
        if ($list.children('.msg').last().attr('data-day') !== key)
            $list.append($('<li class="day-sep"></li>').attr('data-day', key).text(dayLabel(key)));
        $list.append($item);
        normalize();
    }

    function confirmMessage($item, m) {
        $item.removeClass('pending failed').attr('data-id', m.id);
        $item.find('.msg-status').empty();
        const sentAt = new Date(m.sentAt);
        $item.find('.msg-time').attr({ datetime: m.sentAt, title: fullText(sentAt) }).text(timeText(sentAt));
    }

    // ---------- Sidebar ----------
    function refreshSidebar() {
        if (!$('#sidebar').length) return;
        $.get('/c/lista', { active: conversationId || undefined }).done(html => $('#sidebar').html(html)); // server-rendered partial
    }

    function updateSidebar(m, countAsUnread) {
        const $item = byData($('#conversation-list > .conversation-item'), 'conversationId', m.conversationId);
        if (!$item.length) { refreshSidebar(); return; } // a conversation this page has never seen
        $item.find('.preview-text').text(m.body.slice(0, 80));
        $item.find('.conversation-time').text(timeText(new Date(m.sentAt)));
        if (countAsUnread) {
            const $badge = $item.find('.unread-badge');
            const count = Number($badge.attr('data-count')) + 1;
            $badge.attr('data-count', count).text(count).attr('aria-label', `${count} não lidas`);
        }
        $item.prependTo('#conversation-list');
    }

    // ---------- SignalR connection ----------
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/chat')
        .withAutomaticReconnect()
        .build();
    const $banner = $('#connection-banner');

    connection.on('messageReceived', function (m) {
        const mine = m.senderId === me;
        if (m.conversationId === conversationId) {
            // My own other tabs also receive my messages, and the hub's reply may arrive after
            // the broadcast. If this clientMessageId is already on screen, just confirm it.
            const $existing = byData($list.children('.msg'), 'cmid', m.clientMessageId);
            if ($existing.length) { confirmMessage($existing, m); updateSidebar(m, false); return; }

            const stick = nearBottom();
            appendMessage(buildMessage(m));
            if (stick || mine) scrollToBottom();
            else $('#new-messages-pill').removeClass('d-none');
            if (document.hasFocus()) markRead();
            updateSidebar(m, false);
        } else {
            updateSidebar(m, !mine);
        }
    });

    connection.on('presenceChanged', function (userId, online) {
        byData($('[data-presence-user]'), 'presenceUser', userId).attr('data-online', online ? 'true' : 'false');
        byData($('[data-presence-text]'), 'presenceText', userId).text(online ? 'online' : 'visto agora há pouco');
    });

    connection.onreconnecting(() => $banner.text('reconectando…').removeClass('d-none'));
    connection.onreconnected(() => {
        $banner.addClass('d-none');
        catchUp();
        refreshSidebar();
    });
    // withAutomaticReconnect gives up after ~45 s; keep trying every 10 s after that.
    let restarted = false;
    connection.onclose(() => {
        restarted = true; // the next successful start() must catch up, like onreconnected does
        $banner.text('sem conexão · tentando novamente…').removeClass('d-none');
        setTimeout(start, 10000);
    });

    function start() {
        connection.start()
            .then(() => {
                $banner.addClass('d-none');
                if (restarted) { restarted = false; catchUp(); refreshSidebar(); }
            })
            .catch(() => {
                $banner.text('sem conexão · tentando novamente…').removeClass('d-none');
                setTimeout(start, 10000);
            });
    }

    // Messages sent while we were disconnected: ask for everything after the last one we have.
    function catchUp() {
        if (!conversationId) return;
        const lastId = Math.max(0, ...$list.children('.msg[data-id]').map((_, el) => Number(el.dataset.id)).get());
        const stick = nearBottom();
        $.get(`/c/${conversationId}/messages`, { after: lastId }).done(function (html, _status, xhr) {
            if (xhr.getResponseHeader('X-Has-More') === 'true') { location.reload(); return; }
            $($.parseHTML(html)).filter('li').each(function () {
                if (this.classList.contains('msg')) {
                    // Already on screen as "enviando…"/failed? The server has it, so mark it sent.
                    const $existing = byData($list.children('.msg'), 'cmid', this.dataset.cmid);
                    if ($existing.length) {
                        $existing.removeClass('pending failed').attr('data-id', this.dataset.id).find('.msg-status').empty();
                        return;
                    }
                }
                $list.append(this);
            });
            normalize();
            if (stick) scrollToBottom();
        });
    }

    // ---------- Sending: optimistic bubble, then invoke ----------
    function send($item, body, clientMessageId) {
        $item.removeClass('failed').addClass('pending');
        $item.find('.msg-status').text('enviando…');
        connection.invoke('SendMessage', conversationId, body, clientMessageId)
            .then(m => confirmMessage($item, m))
            .catch(function (err) {
                // HubException messages arrive as "... HubException: <our pt-BR text>".
                const text = String(err && err.message || '');
                const reason = text.includes('HubException: ') ? text.split('HubException: ').pop() : 'não enviada';
                $item.removeClass('pending').addClass('failed');
                // Retry reuses the SAME clientMessageId: if the first try was in fact saved,
                // the server returns that message instead of storing a second copy.
                const $retry = $('<button type="button">tentar novamente</button>')
                    .on('click', () => send($item, body, clientMessageId));
                $item.find('.msg-status').empty().append($('<span></span>').text(reason + ' · '), $retry);
            });
    }

    $('#composer').on('submit', function (e) {
        // Not connected (yet)? Let the normal form POST happen: same MessageService on the server.
        if (connection.state !== signalR.HubConnectionState.Connected) return;
        e.preventDefault();
        const body = $input.val().trim();
        if (!body || body.length > 2000) return;

        const clientMessageId = newId();
        const $item = buildMessage({
            clientMessageId, senderId: me, body, sentAt: new Date().toISOString(),
            senderHue: 0, senderInitials: '', senderDisplayName: '',
        });
        appendMessage($item);
        scrollToBottom();
        $input.val('');
        updateComposer();
        send($item, body, clientMessageId);
    });

    // ---------- Load older (keyset paging), keeping the scroll position anchored ----------
    function loadOlder() {
        if (loadingOlder || $('#load-older').hasClass('d-none')) return;
        const firstId = $list.children('.msg[data-id]').first().attr('data-id');
        if (!firstId) return;
        loadingOlder = true;
        $.get(`/c/${conversationId}/messages`, { before: firstId })
            .done(function (html, _status, xhr) {
                const el = $scroller[0];
                const heightBefore = el.scrollHeight, topBefore = el.scrollTop;
                $list.prepend($.parseHTML(html)); // server-rendered partial: Razor already encoded the text
                normalize();
                // The content above grew: move the scroll by the same amount so nothing jumps.
                el.scrollTop = topBefore + (el.scrollHeight - heightBefore);
                $('#load-older').toggleClass('d-none', xhr.getResponseHeader('X-Has-More') !== 'true');
            })
            .always(() => { loadingOlder = false; });
    }

    $('#load-older a').on('click', function (e) { e.preventDefault(); loadOlder(); });
    $scroller.on('scroll', function () {
        if ($scroller.scrollTop() < 80) loadOlder();
        if (nearBottom()) $('#new-messages-pill').addClass('d-none');
    });
    $('#new-messages-pill').on('click', function () { scrollToBottom(); $(this).addClass('d-none'); });

    // ---------- Composer: Enter sends, Shift+Enter adds a line, live counter ----------
    function updateComposer() {
        if (!$input.length) return;
        const length = $input.val().length;
        $('#char-counter').text(`${length}/2000`).toggleClass('over', length > 2000);
        $input.css('height', 'auto').css('height', Math.min($input[0].scrollHeight, 160) + 'px');
    }

    $input.on('input', updateComposer);
    $input.on('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey && !e.originalEvent.isComposing) {
            e.preventDefault();
            $('#composer').trigger('submit');
        }
    });

    // ---------- Copy button (pair link) ----------
    $(document).on('click', '[data-copy]', function () {
        const $button = $(this);
        navigator.clipboard?.writeText($($button.data('copy')).val()).then(() => $button.text('Copiado!'));
    });

    // ---------- Start ----------
    if (conversationId) {
        if (!new URLSearchParams(location.search).has('before')) scrollToBottom();
        markRead();
        $(window).on('focus', markRead);
        $input.trigger('focus');
    }
    start();
})();
